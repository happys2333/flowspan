# Native Remote Window Media Contracts Evidence - 2026-08-27

## Evidence status and boundary

Classification: **local portable contract**, **hosted portable contract**,
**headless Desktop**, and **unsigned package**.

Branch: `codex/v1-foundation`

Implementation commit:
`c269209dff51c51944a69d5ce2293bf0c2b803f4`, based on
`81d9316655ba180ad261933f954668e92d9abfd5`. Final verified repair commit:
`ec2ccbaef8c199d3d0d3c3d38053e9f372d72bc6`.

This record is scoped to task 4 in
`specs/v1/native-remote-window/tasks.md`. It proves the protocol-1.6 media-route
feature gate, fixed `FSM1` attachment envelopes, bounded single-use route
registry, purpose-separated media-session derivation, bounded JPEG codec, fixed
fixtures, same-process authenticated loopback, and deterministic ownership
contracts described below.

It does not prove that the production listener classifies `FSM1`, that a live
Desktop control connection owns a media route, or that Flowspan captures,
renders, or controls a native window. It is not physical two-Device, native
permission, protected-surface, secure-input, accessibility, signed installer,
notarization, or interactive-quality evidence.

## Implemented contract

- Protocol 1.6 alone advertises authenticated Remote Window media attachment.
  Protocol 1.5 keeps its frozen control and encrypted-media fixtures but cannot
  register, connect, or accept a media route.
- The canonical `FSM1` request is exactly 200 bytes and its acknowledgement is
  exactly 232 bytes. Both require zero flags and bind the negotiated version,
  directed Device pair, 16-byte route ID, exact Session and Activity IDs, and
  fresh 32-byte initiator and responder nonces under the existing
  purpose-separated media keys.
- The process-local registry defaults to 32 routes with a 128 hard cap, a
  30-second default and two-minute maximum TTL, and separate 512-entry consumed
  route-ID and initiator-nonce histories. Routes are single-use, matched failed
  claims stay consumed, live owners retain their history slot through cleanup,
  and full security history fails new admission closed.
- Registration, expiry, claim, revoke, timeout, cancellation, disposal, stream
  cleanup, and timer-arm failures preserve exact ownership. Concurrent owners
  join cleanup; primary and cleanup failures remain observable instead of being
  overwritten.
- A same-process integration performs the real authenticated protocol-1.6 TCP
  handshake, transfers the derived media session, then composes a second
  loopback listener, `FSM1` request/acknowledgement, and one bound encrypted
  synthetic media frame. This is a cryptographic and ownership join, not the
  production listener or Desktop runtime.
- SkiaSharp 3.119.4 is a direct locked dependency. Encoding uses only the fixed
  quality/scale ladder: original dimensions at quality 82, 68, and 54, then
  three-quarter and half scale at quality 68 and 54. No candidate may exceed the
  1-MiB encoded logical-frame limit.
- The decoder structurally accepts one complete TopLeft still JPEG only. It
  rejects other formats, animation, truncation, concatenation, trailing data,
  unsafe orientation, invalid marker lengths, dimensions over 16,384,
  more than 16,777,216 pixels, or more than 64 MiB of BGRA output before pixel
  allocation.
- Encoded and decoded managed owners clear borrowed bytes on idempotent disposal.
  Codec source/scaled pixel spans, native encoded copies, failed decode buffers,
  and pooled scratch are cleared before release. Non-cooperative encrypted I/O
  retains borrowed buffers until the underlying operation actually completes.
- The attached media session has no live rekey. Task 5 must close the attachment
  and owning authenticated control connection before either direction exceeds
  `2^20` protected frames, 1 GiB plaintext, or a sequence/epoch boundary, then
  establish fresh authenticated control and media sessions with a new route.

The exact decisions are frozen in ADR 0024 and ADR 0025. Requirements,
architecture, traceability, threat controls, and test boundaries are recorded in
`specs/v1/native-remote-window/`, `docs/security/threat-model.md`, and
`docs/testing/test-strategy.md`.

## Local candidate gates

Environment:

```text
Host: macOS 26.6.2 (build 25G83), Apple Silicon, Asia/Hong_Kong
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Verification date: 2026-08-27
Implementation commit: c269209dff51c51944a69d5ce2293bf0c2b803f4
Final verified repair: ec2ccbaef8c199d3d0d3c3d38053e9f372d72bc6
```

The final candidate tree ran:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore \
  --logger "trx;LogFilePrefix=local" \
  --results-directory \
  artifacts/test-results/2026-08-27-windows-threadpool-fix-local-1
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

- Locked restore and full format verification passed.
- All 26 projects built in Release with 0 warnings and 0 errors.
- Structured XML parsing of 12 fresh TRX files reported `1802` total, `1802`
  executed, and `1802` passed. Every failed, error, timeout, aborted,
  inconclusive, passed-but-aborted, not-runnable, not-executed, disconnected,
  warning, completed, in-progress, and pending counter was zero.
- Per-project results were Desktop 446, Transport 386, Integration 338,
  Platform 219, Security 129, Release 71, Domain 60, Protocol 60,
  Platform.Windows 27, Platform.Linux 27, Platform.macOS 25, and mDNS 14.
- The focused media attachment suite passed `37/37`; the focused JPEG codec
  suite passed `41/41`.
- The repaired controlled-cancellation interleaving passed `30/30` independent
  testhost processes. The complete Desktop suite passed `446/446`.
- Explicit Desktop composition printed its required `TEST MODE` success line.
  The deterministic simulator reported protocol `1.6`, source preserved, target
  resumed, and atomic swap committed.
- The direct/transitive NuGet vulnerability query covered all 26 projects and
  found no known vulnerable package. `git diff --check` passed.

The ignored local TRX directories are:

```text
artifacts/test-results/2026-08-27-native-media-contract-c269209
artifacts/test-results/2026-08-27-windows-threadpool-fix-local-1
```

These results are same-host macOS and portable-contract evidence. A
platform-named contract assembly is not proof that its native adapter ran.

## Windows scheduling finding

The first exact implementation run
[`33084149353`](https://github.com/happys2333/flowspan/actions/runs/33084149353),
attempt 1, failed only in Windows job
[`98559074177`](https://github.com/happys2333/flowspan/actions/runs/33084149353/job/98559074177).
The same SHA failed again in attempt 2, Windows job
[`98563380159`](https://github.com/happys2333/flowspan/actions/runs/33084149353/job/98563380159).
Both failures timed out at
`DesktopPairingDecisionSourceTests.CancellationCoalescingRetainsTheHighestAllocatedSequence`
while waiting for `Task.Run(firstCancellation.Cancel)` to enter its controlled
blocking hook. Every other test passed; macOS, Ubuntu, Secret Scan, and the
separate CodeQL run succeeded.

The test deliberately blocks the cancellation caller at a barrier while the
main test creates a later cancellation to prove sequence coalescing. A shared
ThreadPool work item could not be relied upon to enter that synchronous blocking
section within five seconds under the Windows testhost load. Repair commit
`ec2ccbaef8c199d3d0d3c3d38053e9f372d72bc6` uses the repository's existing
dedicated `LongRunning` test-thread pattern and releases and joins the barrier in
`finally`. It changes no production code and does not relax the five-second
assertions. Thirty independent local testhosts and the all-new exact-commit
Windows run below passed.

## Hosted exact-commit evidence

Final repair commit `ec2ccbaef8c199d3d0d3c3d38053e9f372d72bc6` passed
[CI run `33086310636`](https://github.com/happys2333/flowspan/actions/runs/33086310636),
attempt 1:

- Windows test job
  [`98566784772`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98566784772);
- Ubuntu test job
  [`98566784878`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98566784878);
- macOS test job
  [`98566784947`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98566784947);
- Secret Scan job
  [`98566784980`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98566784980);
- `win-x64` package job
  [`98568205482`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98568205482);
- `osx-arm64` package job
  [`98568205502`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98568205502);
- `linux-x64` package job
  [`98568205471`](https://github.com/happys2333/flowspan/actions/runs/33086310636/job/98568205471).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.6 simulator, and uploaded TRX evidence. Every
package job verified content-locked tooling, published and smoke-tested a
self-contained target, sealed and compared two reproducible unsigned outputs,
audited direct/transitive dependencies, and uploaded one test package.

Downloaded TRX and SARIF artifacts were parsed with XML and JSON parsers.
`Artifact digest` is GitHub's service-computed SHA-256. `Tree SHA-256` hashes a
sorted manifest of every extracted relative path and file SHA-256.

| Artifact | ID | Artifact digest | Tree SHA-256 | Parsed result |
| --- | ---: | --- | --- | --- |
| Windows TRX | `9652440217` | `d48cb3e615cfe4c7949bd0df708668a45c2a2cb1597ae32633b6eda81269faa5` | `597c63d9291499b486bbae99397ff6bf866e85ee0db5a4bb1548247e0d7428b7` | 12 files, 1802/1802 passed |
| macOS TRX | `9652411331` | `ae7aba3ea0685ab601037da3a64ba35f4440029666100990be5d56986f2a5efc` | `229f6898c68d5ebf4f2f404c320fff4fcb8af8f1570afb3b57e6bf9781573d02` | 12 files, 1802/1802 passed |
| Ubuntu TRX | `9652408989` | `8b886e55840734006e1fcd6e32c903460f5ea9d708d482241e10b1a94ca2b298` | `0c0afdc6a2f6d8d113c6dd962a13103ad8894d732c7a19d592e35c9cd1772d49` | 12 files, 1802/1802 passed |
| Gitleaks SARIF | `9652287982` | `00e0d7c5bf6ff8ab0588f8140faf7d2ee13be9319a61942ebc6b21feb1046403` | `49424e51411d8baac254af99c5d621befe644dd8291e9d20e52346d7c0ba7f83` | SARIF 2.1.0, 208 rules, 0 results |

All three TRX aggregates reported the same 12 assemblies and zero unsuccessful
or indeterminate counters; the 36 files total `5406/5406` passes. Gitleaks SARIF
contains no invocation record, so execution success is established by the
hosted Secret Scan job rather than inferred from the zero-result SARIF alone.

All three downloaded package directories independently passed
`Flowspan.Release verify`. All 15 non-manifest entries across their three
`SHA256SUMS` files matched. Their in-toto/SLSA v1 provenance binds version
`0.1.140`, exact commit `ec2ccbaef8c199d3d0d3c3d38053e9f372d72bc6`, CI run
`33086310636`, attempt 1, the expected RID, and the named hosted builder.

| Package | ID | Artifact digest | Tree SHA-256 | Inner archive bytes / SHA-256 |
| --- | ---: | --- | --- | --- |
| `win-x64` | `9652533558` | `67c136c68fbc884abb1a0651fa40faf4999964c4d02b85d441e63cebf92678f2` | `d760ed3eec058f4bec729372802a9afb1e9213d2ef09bf80d5e7e1c3ce6375c6` | 43,913,885 / `c1c14b79cd021c170161786fa7bc3ce5ed1352733e61b8c056de4f39e0632b0a` |
| `osx-arm64` | `9652519461` | `1c876a0d252695768ef33c73200c109eb1ffc06860ee62fd415a063c646be9f7` | `241eed33374f2ec4cc1268312604d32383972f8aab6011d8f2a370e1d7cda272` | 42,745,797 / `f32d4bef06d1b47e4a3e0c7a80bec7e0c83d23f743c42f243d34920608e2ff83` |
| `linux-x64` | `9652521386` | `a39ff03f15ace2e6aa28a66fbec05fdaa0011e6818634b623628965292965282` | `2d13ecb5654a553e4c047b34005331d11c0ee5cb4f6027ea466c0d602a64a897` | 41,925,813 / `9e05b6dc150e98c2132de575723771a7f16fcfe2f3b3ecf6b228bd4bd81f75be` |

Each SPDX 2.3 SBOM contains 38 packages and 38 relationships: one application,
four direct dependencies, and 33 transitive dependencies. Each license report
contains the application plus 37 dependency entries and remains
`reviewRequired=true`. The application license is undeclared; 28 dependencies
have declared expressions, while one declared file, four legacy URLs, and four
missing declarations still require human review. All packages are explicitly
`unsigned-test-artifact`; none is a signed or notarized installer.

[CodeQL run `33086310637`](https://github.com/happys2333/flowspan/actions/runs/33086310637),
job
[`98566785707`](https://github.com/happys2333/flowspan/actions/runs/33086310637/job/98566785707),
also passed for the exact repair commit. CodeQL 2.26.4 analysis `1682276047`
evaluated 52 rules and reported 0 results; the branch had 0 open alerts at the
time of verification.

These hosted results prove portable build, contract, headless composition, and
reproducible unsigned-package behavior on the named runner images. They do not
prove a native capture, input, protection, permission, physical-device, or
signed-package path.

## Independent review

Four independent read-only reviews of the implementation, codec, security
ownership, and standards/spec conformance reported no blocking finding. The
Windows repair was separately checked against the repository's dedicated-thread
test pattern and preserves failure cleanup through `finally`.

## Open evidence

- Task 5 must compose the production same-listener `FSM1` selector, live
  control-owned route registration, capacity-one logical-frame encoder path,
  transport/reassembly, decoder, renderer, native safety gates, and ordered
  teardown. Production Remote Window remains unavailable until that path is
  complete.
- Tasks 6-8 must implement and prove native macOS, Windows, Wayland, and explicit
  X11 permission, capture, input, protection, and Emergency Stop behavior on
  matching real machines.
- Task 9 must provide cross-platform fault/load measurements and physical
  two-Device evidence for exact signed or notarized package digests.
- Cross-implementation protocol review, human license review, signed/notarized
  installation lifecycle, native accessibility, affected parent tasks, release
  criteria, task 9.4, and the long-term v1 Goal remain open.

This record supports completion of native Remote Window task 4 only. It does not
close tasks 5-10, any native or physical release criterion, or Flowspan v1.
