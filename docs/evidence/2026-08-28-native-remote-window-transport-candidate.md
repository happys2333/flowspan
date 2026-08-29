# Native Remote Window Transport Candidate Evidence - 2026-08-28

## Evidence status and boundary

Classification: **local and hosted portable contract candidate** plus **same-host
managed loopback candidate**.

Branch: `codex/v1-foundation`

Base commit: `645b065571a08f777a8272d171be5d5151826bd6`.

Transport implementation commit for Tasks 5.1-5.3:
`f4307052c9660475ab07d6d8ea42234fede0a890`.

Media-budget recovery implementation commit for Task 5.4:
`a75afb142c335d8da71e511c29e51b14ad2b3cf7`.

The local gates below ran against the exact Task 5.4 implementation tree before
that tree was committed without content changes. The separately recorded hosted
gates compile and execute the same portable contracts, but neither evidence set is
native-platform, physical-device, signed-package, or release evidence.

This candidate supports only Task 5.1-5.4 in
`specs/v1/native-remote-window/tasks.md`. Task 5 and Flowspan v1 remain open.

## Implemented Transport slice

- Each authenticated protocol-1.6 control registration transfers its
  purpose-separated Remote Window media session into one connection-owned entry.
  Protocol 1.5 and earlier remain unable to publish or attach a media route.
- The production `FlowspanTcpInboundListener` classifies the fixed `FSM1`
  attachment on the same published endpoint as pairing and authenticated control,
  with independent bounded media capacity and exactly one owner for the pre-read
  envelope and accepted stream.
- Responder route registration and initiator attachment bind the directed Device
  pair, route, Session, and Activity. Expiry, revoke, matched attachment failure,
  media send/receive fault, disposal, or listener shutdown consumes the route,
  releases the media owner, and requests stop of the owning control connection.
- Attachment and listener cleanup preserve a single original failure and aggregate
  distinct primary and cleanup failures. Caller cancellation, deadline, and
  session disposal stop waiting on token-ignoring async handshake I/O while
  detached borrowed buffers remain stable until the underlying operation settles
  and are then cleared.
- One logical video frame owns 1 through 1,048,576 bytes and is split into at most
  16 chunks of at most 65,536 bytes. The assembler requires the exact binding,
  video kind, count, zero-based order, continuous sequence, non-empty chunks, and
  aggregate bound; it consumes and clears chunks and partial state on every path.
- The logical sender owns one active frame and only the latest pending frame. It
  sends one wire chunk at a time through the existing peer/session-budgeted queue,
  reports bounded outcomes, clears replaced and terminal owners, and prevents
  logical-frame interleaving.
- Stop/disposal initiation returns while a cancellation callback is temporarily
  blocked. Cleanup completion joins that callback and any borrowed send, then
  attempts worker, sink, budget, and cancellation-source cleanup; throwing stages
  are aggregated. Active and pending submissions settle and the same peer can
  create a fresh queue after completed cleanup.
- A test-only media-session profile can reduce, but cannot raise or disable, the
  frozen production limits of `2^20` protected frames and 1 GiB of plaintext per
  direction and epoch. Public production entry points retain the frozen limits.
- The recovery proof covers frame-count and plaintext exhaustion in both media
  directions. The reduced frame limit is exactly 2. The reduced plaintext limit
  is exactly 220 bytes: the 128-byte request or 160-byte acknowledgement plus one
  60-byte media frame fits, and the next media frame does not.
- At the hard bound, encryption fails before writing another byte. The media
  attachment faults, both connection-owned media entries and the responder route
  drain, both old control registrations become unusable, and the listener's
  single authenticated-session slot becomes available only after that teardown.
- Recovery performs a second complete authenticated control handshake under the
  same reduced policy. It derives a different media Session identifier and route,
  rejects the old `FSM1` request while the new route remains live, produces a
  different attachment request, and transfers a new media frame in the tested
  direction. An independent handshake test proves that ciphertext from the old
  media session is rejected by the fresh transcript-derived session while fresh
  ciphertext succeeds at epoch 1.

## Local candidate gates

The final candidate tree is required to run:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Debug --no-restore
dotnet test tests/Flowspan.Transport.Tests/Flowspan.Transport.Tests.csproj \
  --configuration Release --no-restore
dotnet test tests/Flowspan.Security.Tests/Flowspan.Security.Tests.csproj \
  --configuration Debug --no-restore
dotnet test tests/Flowspan.Security.Tests/Flowspan.Security.Tests.csproj \
  --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

Environment:

```text
Host: macOS 26.6.2 (build 25G83), Apple Silicon, Asia/Hong_Kong
.NET SDK: 10.0.301
Branch: codex/v1-foundation
Verification date: 2026-08-28
Base commit: 645b065571a08f777a8272d171be5d5151826bd6
Transport implementation commit: f4307052c9660475ab07d6d8ea42234fede0a890
Budget recovery implementation commit: a75afb142c335d8da71e511c29e51b14ad2b3cf7
```

Observed results on that final local tree:

- Locked restore, full format verification, and `git diff --check` passed.
- Release build with warnings as errors completed with 0 warnings and 0 errors.
- The Transport suite passed `460/460` in Debug and `460/460` in Release,
  including all four frame/plaintext-by-direction budget-recovery cases.
- The Security suite passed `131/131` in Debug and `131/131` in Release,
  including immutable usage-limit bounds and fresh-handshake ciphertext isolation.
- The complete Release solution passed `1878/1878`.
- The direct/transitive vulnerability query covered all 26 projects and found no
  known vulnerable NuGet package.

These results are same-host macOS managed-code evidence. Platform-named contract
assemblies did not execute native Windows or Linux Remote Window adapters here.

## Hosted exact-commit evidence

Implementation commit `a75afb142c335d8da71e511c29e51b14ad2b3cf7`
passed [CI run `33109385771`](https://github.com/happys2333/flowspan/actions/runs/33109385771),
attempt 1:

- macOS test job
  [`98647841899`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98647841899);
- Ubuntu test job
  [`98647841984`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98647841984);
- Windows test job
  [`98647842018`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98647842018);
- Secret Scan job
  [`98647842002`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98647842002);
- `win-x64` package job
  [`98648935964`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98648935964);
- `linux-x64` package job
  [`98648936026`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98648936026);
- `osx-arm64` package job
  [`98648936061`](https://github.com/happys2333/flowspan/actions/runs/33109385771/job/98648936061).

Every test job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.6 simulator, and uploaded TRX evidence. Every
package job verified content-locked tooling, published and smoke-tested a
self-contained target, sealed and compared two reproducible unsigned outputs,
audited direct/transitive dependencies, and uploaded one test package.

Downloaded TRX and Secret Scan artifacts were parsed with XML and JSON parsers.
Artifact digests below are the SHA-256 values reported by the GitHub artifact API,
and every artifact is bound to the exact implementation SHA.

| Artifact | ID | Artifact digest | Parsed result |
| --- | ---: | --- | --- |
| Windows TRX | `9661966478` | `ca47bc6b38db5f2b7218cb538008a4286041c89678f17c54cbd60b2dcce31825` | 12 files, 1878/1878 passed |
| macOS TRX | `9661964740` | `5c98efbc9a936ae85de7c78edb6fd97f0443157f5dfda2a82c00a203a3d5e4ca` | 12 files, 1878/1878 passed |
| Linux TRX | `9661948856` | `964064f605084324d83b83014cec8a3b00bd76b3814e47eafa9fc4473bb43bb6` | 12 files, 1878/1878 passed |
| Gitleaks SARIF | `9661844851` | `0d615184eb63fac8dc8388d17f8688dee1eeb05e84e6602ee3af13da663622ac` | 208 rules, 0 results |

Every hosted platform aggregate reported zero failed and zero not-executed/skipped
tests. Each had the same per-project counts: Desktop 446, Transport 460,
Integration 338, Platform 219, Security 131, Release 71, Domain 60, Protocol 60,
Platform.Windows 27, Platform.Linux 27, Platform.macOS 25, and mDNS 14.

The reproducible unsigned package artifacts are also bound to that workflow SHA:

| Artifact | ID | Artifact digest |
| --- | ---: | --- |
| win-x64 unsigned test package | `9662040909` | `a8ace0c1c34b4c8506011a49c2b32a50b8698879c49bbc98d13c146199fd4d4b` |
| linux-x64 unsigned test package | `9662020083` | `b2487dcf15a45b53251f3ff3688ac46a67a34fad1bb8cfb2f9f7c861ca5dd4bd` |
| osx-arm64 unsigned test package | `9662038548` | `a39315e8fd5ce872b38ba6dc5d01c0ad64142fd65f4d09f7ab6d61fe1ace572e` |

Each downloaded `SHA256SUMS` verified. Its sealed application archive was:

| RID | Archive | Size | Archive SHA-256 |
| --- | --- | ---: | --- |
| `win-x64` | `flowspan-0.1.145-win-x64-unsigned-test.zip` | 43,933,446 | `9aa3f414d38d6c5997d4532e62304fe67bda4eb71065409bec2906f77b58e5a9` |
| `linux-x64` | `flowspan-0.1.145-linux-x64-unsigned-test.tar.gz` | 41,944,636 | `0d28da4ba8d135aa5372b3946c6910fcfac2d0fe44005bb3fb795a543fbb97f0` |
| `osx-arm64` | `flowspan-0.1.145-osx-arm64-unsigned-test.tar.gz` | 42,765,655 | `210c05404ceca715a703afe254a88a6032a45ae61d085a85da53fd1192881990` |

The internal package, update, and SLSA provenance manifests agree on schema
`flowspan.package/v1`, version `0.1.145`, build `0.1.45`, the exact commit, CI
channel, RID, builder, archive digest and size, and invocation
`33109385771/attempts/1`. Their signature state is explicitly
`unsigned-test-artifact`. License and SPDX reports cover 38 packages; their
expected `reviewRequired=true` records that Flowspan's application license is
still undeclared.

[CodeQL run `33109385769`](https://github.com/happys2333/flowspan/actions/runs/33109385769),
job [`98647841812`](https://github.com/happys2333/flowspan/actions/runs/33109385769/job/98647841812),
also passed for the exact implementation SHA. Analysis `1683817223` evaluated
52 rules and reported 0 results and 0 open branch alerts.

These hosted results prove portable builds, managed contract behavior, Secret
Scan, CodeQL, and reproducible unsigned packaging on the named runner images.
They do not prove that a native Remote Window adapter was constructed or used,
nor do they prove physical networking, packaged accessibility, or release
readiness.

## Subsequent exact-binding replacement-generation checkpoint

Test-only commit `ba58562aff020e3cd9fcc5c8066bcfe74d692b8b` adds one
production Transport boundary contract without changing production source. It
creates a real authenticated protocol-1.7 control generation and a signed,
verified loopback endpoint, completes `FSM1` route acceptance, and blocks the old
attachment after
the route registry has marked it Attached but before the authenticated media
directory publishes it. The old host lease then fail-closes. Both control run
loops stop, directory, route, and registered-peer ownership drains, and retained
leases become non-current while that accepted attachment remains blocked.

The same Device pair reconnects through strictly higher control generations and
reuses the same Session and Activity with a fresh Route ID. A second independent
gate proves that the replacement route is Attached, its participant side is
attached, and its current host directory entry remains unattached. At the media-
binding level, only the Route ID differs. Releasing only the old gate produces the
expected `InvalidDataException` because the old exact binding has no live owning
control connection. The current replacement generation remains current and
unattached, retains exactly one live route, and neither control stop token is
cancelled. Releasing the replacement gate then attaches the exact new binding and
transfers one encrypted media frame successfully. Explicit replacement fail-close
drains the replacement generation and all published owners. Bounded final
teardown then disposes every retained connection, lease, handler, directory,
route registry, and frame owner.

This is one isolated exact-binding ABA row. Composed with the Desktop tracer's
cleanup-before-publication row, it proves that a delayed accepted attachment
cannot retarget one already prepared replacement generation. It is not a full
Desktop renderer-to-replacement trace and does not cover every Session, Activity,
Device, reconnect, cleanup-fault, or boundary-failure combination. The managed
Desktop tracer remained thirteen cases at this `ba58562` checkpoint. A later
Desktop-only cleanup-fault case is recorded in the managed tracer evidence; it
does not expand this Transport ABA row.

A still later test-only Desktop checkpoint
`6ff3fefaa667e23f309681fe5fe953ae97bb5861` now composes the same
production exact-binding boundary through a single end-to-end
renderer-failure-to-replacement trace. That fifteenth managed case uses fresh
Session, Correlation, and Route IDs for the replacement and proves that releasing
the delayed old attachment cannot attach to, stop, or admit the replacement;
the replacement attaches and transfers encrypted media only after its own gate is
released. This strengthens the composed Desktop evidence but does not change the
`ba58562` Transport contract, its historical thirteen-case count, or the open
Session/Activity/Device/reconnect/cleanup-fault matrix. Exact-SHA CI
`33266348260` and CodeQL `33266348243` for the later Desktop checkpoint both
passed; the managed tracer evidence records parsed TRX, Secret Scan, package,
analysis, artifact, and digest details.

Test-only Desktop commit `13681fb451df53290496416d11837ffb5435e500`
subsequently adds a sixteenth managed tracer row for active disconnect by one
capture Emergency Stop cleanup fault. It changes no production source and does
not expand the Transport exact-binding ABA contract. Exact-SHA CI `33267557804`
passed `2240/2240` on every hosted OS plus Secret Scan and all unsigned package
jobs; CodeQL `33267557806` passed 52 rules with 0 results and 0 exact-commit open
alerts. Detailed artifacts are recorded in the managed tracer evidence. This
closes one additional cleanup owner only; the Transport/replacement cleanup-fault
cross-products remain open.

The first fixture RED deliberately reused one gate for both generations. It
failed deterministically after about 62 ms with expected forward count 1 and
actual 2, demonstrating that the fixture could not independently release the old
attachment. The two-gate harness was GREEN against the existing production
guard; this was not a production defect. Mutation verification then removed only
the production exact-binding inequality check. The focused test failed after its
five-second bound because the old attachment polluted the replacement, and the
guard was immediately restored. Final local macOS verification recorded:

- focused Debug and Release `1/1`;
- the containing media-session class Debug and Release `29/29`;
- Transport Debug and Release `702/702`;
- 80 fresh Debug processes at eight-way concurrency, `80/80`;
- complete Debug and Release solutions, `2237/2237` each, including Desktop
  `548/548`;
- Debug and Release warning-as-error builds with 0 warnings and 0 errors; and
- format, diff, direct/transitive NuGet vulnerability, explicit TEST MODE
  composition, and protocol-1.7 simulator gates passed.

Exact-SHA CI run
[`33261748925`](https://github.com/happys2333/flowspan/actions/runs/33261748925)
has two attempts. Attempt 1's Ubuntu, Windows, and Secret Scan jobs succeeded,
but macOS job
[`99124771411`](https://github.com/happys2333/flowspan/actions/runs/33261748925/job/99124771411)
was terminated with exit 137 about 0.15 seconds after `dotnet format` began. Its
locked restore had completed, but it did not build, run tests, or publish TRX;
dependent package jobs were skipped. The unchanged exact SHA was rerun once.
Attempt 2 completed successfully:

| Job | Job ID | Artifact ID | Artifact SHA-256 | Parsed result |
| --- | ---: | ---: | --- | --- |
| Ubuntu tests | `99125319147` | `9717487931` | `0fac27eba022bdbe29f3880ab9de86914eca6fd77d057002a3881d6db0626e53` | 12 TRX, 2237/2237 |
| Windows tests | `99125334605` | `9717494099` | `21d4b30c91035a302797fc5c4013e8419443d658164fe29b07727c739de964ae` | 12 TRX, 2237/2237 |
| macOS tests | `99125318578` | `9717537994` | `db2ccb9915eca9dcc3334b05aea3d6e735d5dce7aa9b206491d5cfd6771afb51` | 12 TRX, 2237/2237 |
| Secret Scan | `99125335087` | `9717446010` | `a024907b129157fccff49855133c95b122afb9155a2f5d8709f64ea404aef222` | SARIF 2.1.0, 208 rules, 0 results |

All parsed test artifacts had zero failed, error, timeout, aborted, or other
non-success results. Attempt 2's reproducible unsigned package jobs also passed:

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `win-x64` | `99125685608` | `9717564252` | `ed15ecb1479fff5977b6609533342065df466a8f61dba0ce605143bff532cd95` |
| `osx-arm64` | `99125685611` | `9717563836` | `2f7e251fa955190e43d7dbc7096e55639052f9b2afc152173c1a7b97c6934d4f` |
| `linux-x64` | `99125685639` | `9717557925` | `ed949325734ee47ded9d3bc8f8aa5e34571f727f7d14edbc5b6c260c96b95aa6` |

[CodeQL run `33261748927`](https://github.com/happys2333/flowspan/actions/runs/33261748927),
job [`99124771368`](https://github.com/happys2333/flowspan/actions/runs/33261748927/job/99124771368),
also passed for the exact SHA. Analysis `1692023991` evaluated 52 rules with 0
results, and the branch open-alert query returned 0. The successful unchanged-SHA
rerun supports the diagnosis that the first macOS termination was a transient
runner/resource failure; the failed attempt remains part of the record and is not
a test result.

## Independent review

Two read-only reviews of the initial Transport tree identified no P0 and one P1:
a second chunk zero silently replaced an incomplete logical frame on the ordered
stream. The candidate now rejects that truncation, clears both owners, and proves
recovery only through a subsequent fresh frame. It also changes registration
cleanup to asynchronous failure-preserving disposal and unconditionally observes
late prefix-write/read/flush faults after cancellation.

Final independent ownership, security, and acceptance reviews of the complete
candidate tree reported no remaining P0 or P1. One P2 diagnostic-duplication
class remains in two ownership wrappers. Normal control-session shutdown can
observe the same media-registration cleanup failure through both dispatcher-owned
cleanup and the joined registration completion. A responder attachment failure
can likewise reach the listener first as its handler failure and then as the
result of idempotently disposing that same attachment. A final aggregate may
therefore contain the same exception twice. The failure remains visible and
cleanup is still joined; this does not leak authority, routes, attachments, frame
owners, or budget.

## Open evidence

- Task 5.5 must compose `ProductionDesktopRemoteWindowService`, exact native source
  lease/revalidation, permission/readiness, controller, capture-to-JPEG encoder,
  authenticated media, decoder-to-participant renderer, protection, independent
  Emergency Stop, visible sharing, input, and ordered teardown. Production
  composition must continue to report `native_adapters_unavailable` until then.
- The current tests do not prove completed teardown if a cancellation callback or
  synchronous third-party `Stream.Dispose` implementation blocks forever. They
  prove prompt stop/disposal initiation for the injected callback case, followed
  by complete joined cleanup after release. They do not prove native Windows,
  macOS, Wayland, or X11 behavior.
- Budget exhaustion and cleanup-fault handling are each covered, but their full
  cross-product is not. A simultaneous budget-bound failure plus an injected
  throwing cleanup stage remains an explicit fault-injection combination to add.
- One Transport exact-binding replacement-generation row and one full Desktop
  renderer-to-replacement causal trace are covered. The other replacement/ABA
  variants and their cleanup-fault cross-products remain open.
- Physical two-device loss/reconnect/load/latency, native accessibility, signed or
  notarized package lifecycle, Tasks 6-10, affected release criteria, and the
  long-term Goal remain open.
