# macOS exact-window capture candidate — 2026-10-04

Status: bounded native candidate and matching-host smoke evidence, **not**
production host availability, complete Task 6, physical-device acceptance or a
release. Task 6, its safety/permission/input/package gates and the v1 Goal remain
open. See [ADR 0029](../adr/0029-direct-csharp-screencapturekit-interop.md), the
[native spec](../../specs/v1/native-remote-window/requirements.md) and the
[reproducer](../../tools/Flowspan.MacOS.NativeCaptureProbe/README.md).

## Exact implementation and local managed checks

Candidate implementation is `3418f2044bcb70047b969e8873209c5ef86453b9`; minimum
first-frame repair is `f8f3bd16a8fd2f200029e8ebc4cd430e0546fd8d`.
Root verified the latter from a clean detached checkout on macOS 27.0.1 ordinary
arm64, .NET SDK 10.0.301 / runtime 10.0.9. No Windows/Linux execution is claimed
by this local result.

| Check | Debug | Release |
| --- | --- | --- |
| Complete solution | 2686/2686 | 2686/2686 |
| MacOS test project | 126/126 | 126/126 |
| Capture/catalog candidate cases | 62/62 | 62/62 |
| New first-frame regression cases | 4/4 | 4/4 |
| Warning-as-error build | 0 warnings/errors | 0 warnings/errors |
| Explicit TEST MODE composition | Passed | Passed |
| Deterministic simulator | Passed | Passed |

Each solution result has 12 independently parsed TRX files, all outcomes Passed
and every failed/error/timeout/aborted/inconclusive/other non-success counter
zero. Locked restore and full-solution format passed. A separate direct/transitive
NuGet audit listed zero vulnerabilities across 26 projects. This is not a local
gitleaks or native-fault-injection result. Candidate ownership reviews found no
remaining definite P1/P2 in the reviewed slice; reviewers did not execute tests
and this is not an independent external security audit.

Reproduction used:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
for configuration in Debug Release; do
  dotnet build Flowspan.slnx --configuration "$configuration" --no-restore
  dotnet test Flowspan.slnx --configuration "$configuration" --no-build --no-restore \
    --logger "trx;LogFilePrefix=macos-prestart-$configuration" \
    --results-directory "/tmp/flowspan-macos-candidate-f8f3bd1/$configuration" \
    --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
  dotnet run --project src/Flowspan.Desktop --configuration "$configuration" \
    --no-build --no-restore -- --validate-composition
  dotnet run --project src/Flowspan.Simulator --configuration "$configuration" \
    --no-build --no-restore
done
dotnet list Flowspan.slnx package --vulnerable --include-transitive --format json
```

TRX and audit evidence are retained under
`/tmp/flowspan-macos-candidate-f8f3bd1/` on this machine.

## Observed failure and regression repair

The final proxy-labeled tool initially failed in Debug: direct native capture
passed, but the catalog/boundary marker deadline expired. That actual failed
run is preserved at
`/tmp/flowspan-native-capture-probe-proxy-label-debug-failure-20261004.md`.
Release of that exact tool input had not run at failure time. Earlier successful
temporary probes do not establish that this candidate worked.

A static window can deliver its only Complete sample before native Start
completion; the boundary rejected it while delivery was closed. Subsequent Idle
samples do not carry a new complete frame. Four deterministic tests forced that
call-site sequence without a later sample: successful, failed, canceled and
stopped startup. All four actually failed before the change (expected retained
sample DisposeCalls 0, actual 1), then passed after the minimum boundary change.

The boundary now retains one latest pending sample during startup, without
signaling its worker, copying pixels or delivering a frame. Only successful
native Start, fresh source validation and an open owner generation grant worker
delivery. Failure, cancellation and Stop release pending ownership without
copy/delivery. Neither native driver nor synthetic-window repaint stimulus was
changed. Actual RED/GREEN execution excerpts, explicitly not a complete console
transcript, are in `/tmp/flowspan-macos-prestart-red-green-20261004.md`.

## Actual task-owned native runs after the fix

Both configurations were rebuilt against the repaired platform source. Six fresh
processes actually executed `Flowspan.MacOS.NativeCaptureProbe.dll --run` with
already granted capture permission, on the local ordinary-arm64 host:

| Configuration/run | Exit | Seconds | Raw matched frames |
| --- | --- | --- | --- |
| Debug 1 | 0 | 3.388 | 1 |
| Debug 2 | 0 | 3.540 | 2 |
| Debug 3 | 0 | 3.398 | 1 |
| Release 1 | 0 | 3.307 | 1 |
| Release 2 | 0 | 3.377 | 1 |
| Release 3 | 0 | 3.348 | 1 |

Root independently read all six stdout records and matched their source hashes.
All six validated the four-color task-owned 64-by-64-point window as packed
128-by-128 pixels at scale 2, both directly and through the actual source catalog
and capture boundary. They joined native Stop/drain and boundary StopCompletion,
checked the independent physical IsDrained fact, exercised a Start/Stop race,
preserved absent NSApplication during initial enumeration, excluded this process
by default, and invalidated the hidden task window within the bounded poll.
Final capture callback roots and retained-capture-owner counts were zero.

All six reported zero further downstream `takeSampleOwnership` delivery-proxy
calls and sink frames during the **600 ms** post-drain observation. This proxy
does not instrument every unmanaged DidOutput entry, and the finite observation
does not prove that callbacks can never occur later. The tool requested no
permissions, read no window titles, and wrote no pixel files. Global shareable-
content enumeration can still expose other-window metadata; the capture guard
is task window ID plus own PID, not enumeration isolation.

Records are
`/tmp/flowspan-native-capture-probe-startup-fix-{Debug,Release}-run{1,2,3}-20261004.md`.
Their normal-success operations do not inject real native Start/Stop/remove/
barrier faults; failure-path contract tests are separate managed evidence.

## Source identity, ownership and availability boundary

The catalog binds local window ID, PID, process-start identity, geometry and a
portable unpredictable token/generation. Labels are generic; titles are not
identity. Failed native source/sample owners remain rooted in preallocated
intrusive retention, and failed capture construction can hand back its true
asynchronous rollback owner. The deferred worker recreates its generation-bound
source-use scope and holds it through sample copy and sink delivery. One pending
sample plus one worker bounds delivery ownership.

Synchronous Stop/Pause/Emergency confirmation means only delivery-latch closure.
An external owner must join `StopCompletion` or `DisposeAsync` before releasing
borrowed native resources. IsDrained is a separate monotonic physical fact, not
the absence of a diagnostic failure. Pause closes and drains this capture;
Resume requires a fresh owner rather than silently reusing the old source.

The selected candidate supports ordinary arm64 macOS >=14.2 and requires an
existing AppKit/main-runloop owner. Production host factory remains unavailable;
capture support metadata is not readiness. Input is Unsupported and protection
is Unknown. Still open: TCC preflight/enumeration TOCTOU and possible OS UI,
same-process window-ID ABA, complete source-loss notification, sensitive windows
and secure input, independent local Emergency Stop, older OS/Intel/arm64e,
packaged permission identity and grant/deny/revoke, real UI composition, physical
two-device operation, signing/notarization and release acceptance.

CI defaults never pass `--run`; they only build/format and verify the no-capture
default. The superseding integration commit
`c5332462880f079a3767e7c6cf4c3d4990c457d3` was independently rerun locally from a
clean checkout: both solutions 2686/2686, all four standalone probes built and
formatted, Windows ten/Linux seventeen portable cases passed, and a further
actual Release task-owned macOS native run passed. Its stdout is retained at
`/tmp/flowspan-native-candidate-c533246/native-exact.stdout`.

That commit's [hosted checkpoint](2026-10-04-native-candidate-hosted-ci.md) is
**failed CI**: Windows/macOS each pass 2686/2686, Linux passes 2685/2686 because
of an Activity teardown exception mismatch, and all package jobs are skipped.
All 62 candidate cases pass on every hosted OS. CodeQL passes with measured
421/421 C# extraction; the separate Linux no-capture ABI job truly passes.
Successful substeps do not make the enclosing CI successful, and no hosted
production-driver capture or physical/release proof is claimed.

The later `470d0f3` [hosted checkpoint](2026-10-04-cancellation-wgc-hosted-checkpoint.md)
repairs that separate Activity cancellation case and passes 2692/2692 on all
three OSes, with all 62 macOS candidate cases Passed. This does not erase the
`c533246` failure or add new native capture/safety evidence. The same-source
standalone Windows opt-in run separately fails before capture item creation
completes; complete unsigned-package downloads/inner audits remain pending.

## Final-source SHA-256 anchors

```text
6745b28773f62a9c84a7dfba927fcae1df33a8e0f6d5498519aa548e137a8aa1  CaptureBoundary.cs
5dd801f886efdf25cf91b5b2af22a1518316346e963c388b0ddcde59feb13754  CaptureOwnershipTests.cs
9259b22755487d8ef364353be2ec3c819b84cfd3a50a75478651c1264e940459  ScreenCaptureKitApi.cs
622bb48cd5eb8c0db921a48bfefb45c46cf2abbf1ad7dea58b9908995a72e1b8  SourceCatalog.cs
a76235e081fb0ab9b851b5187fffbc0fe1874d528e0780c7c1985d55e1c1ef73  NativeCaptureProbe/Program.cs
c57e4038d72406a43838be3eb989c2d1267701e2238c9ad6314aa87b3bb06c6a  NativeCaptureProbe/OwnWindowApi.cs
d63d606e078be35e59b59334021030c909b4db08441d757652700231c8428e78  NativeCaptureProbe/TaskWindow.cs
2f7a3771eb0c4f2697333b7560fdbdc8ae672e0e1c908553d739bd1a215c542c  NativeCaptureProbe.csproj
```
