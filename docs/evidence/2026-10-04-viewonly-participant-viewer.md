# Opt-in ViewOnly participant Viewer

Date: 2026-10-04. Branch: `codex/v1-foundation`.
Feature implementation: `94f10dcae192dd5ced81d6a096ab940260808828`.
Verification implementation: `f598d920d97f9909b5dc73856996d3a5612c94c4`.

The verification commit contains the complete Viewer feature and only corrects
the import ordering in `RemoteWindowViewerViewModelTests.cs`. This evidence
checkpoint covers the opt-in ViewOnly participant Viewer; the enclosing native
host, physical-Device, and release gates remain open.

## Local exact-source verification

The clean isolated worktree at verification SHA `f598d92` passed locked
restore and full format verification. On macOS 27.0.1 arm64, build 26A434,
using .NET SDK 10.0.301, Debug and Release solution builds each completed with
zero warnings and errors. Each configuration produced 12 TRX files with 2624
total, executed, and passed tests; every other counter was zero. Desktop is
757/757 and Transport 758/758 in each configuration. The 21 added named cases
below were individually matched with `Passed` outcomes in both local TRX sets.
The managed two-node tracer is now 43/43, including the new Viewer/TCP case.

Release explicit TEST MODE composition, the protocol-1.7 deterministic
simulator, and the direct/transitive NuGet vulnerability audit passed; the
audit reported no known vulnerabilities for all 26 projects. The standalone
probe's Release build and default synthetic-only execution also passed,
including 1,000 copied-Block cycles and 1,000 synthetic sample cycles. That
execution did not call capture preflight, initialize AppKit, or perform native
window capture. The exact worktree's Git status was clean after verification.

Local TRX are retained in `/tmp/flowspan-viewer-exact-f598d92/Debug/` and
`/tmp/flowspan-viewer-exact-f598d92/Release/`; they are temporary evidence, not
durable repository artifacts. Reproduce from a clean checkout of the exact
verification SHA:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore --logger 'trx;LogFilePrefix=local-debug' --results-directory TestResults/Debug --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFilePrefix=local-release' --results-directory TestResults/Release --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive --no-restore
dotnet build tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj --configuration Release
dotnet run --project tools/Flowspan.MacOS.CaptureProbe/Flowspan.MacOS.CaptureProbe.csproj --configuration Release --no-build --no-restore
```

## Hosted verification

### Initial formatting failure

[CI 37199215427](https://github.com/happys2333/flowspan/actions/runs/37199215427),
run 233, attempt 1, completed with `failure` at feature SHA `94f10dc`.
Windows job `111427243084`, macOS job `111427243039`, and Linux job
`111427243018` all report `error IMPORTS: Fix imports ordering.` at line 1 of
`tests/Flowspan.Desktop.Tests/RemoteWindowViewerViewModelTests.cs`. The build,
solution tests, composition check, simulator, and standalone probe steps were
skipped on all three OS jobs; the package matrix was skipped. Secret Scan job
`111427242865` succeeded. No hosted test, probe, or package pass is claimed for
this initial commit.

### Verification commit

[CI 37199481975](https://github.com/happys2333/flowspan/actions/runs/37199481975)
and [CodeQL 37199481994](https://github.com/happys2333/flowspan/actions/runs/37199481994)
are run 234, attempt 1, for exact verification SHA
`f598d920d97f9909b5dc73856996d3a5612c94c4`. Both completed with `success`.
The import-order correction is the only source difference from the original
feature commit; it does not replace or erase the initial formatting failure.

### Downloaded test results

Each downloaded OS archive contains 12 parsed TRX files with 2624 total,
executed, and passed tests. Every other counter is zero (`failed`, `error`,
`timeout`, `aborted`, `inconclusive`, `passedButRunAborted`, `notRunnable`,
`notExecuted`, `disconnected`, `warning`, `completed`, `inProgress`, and
`pending`), and every `UnitTestResult` outcome is `Passed`. Each OS separately
reports 757/757 Desktop and 758/758 Transport tests, with all non-success
counters zero. The downloaded archive bytes match the API digests below, and
every artifact's metadata binds it to the exact verification SHA and CI run.

| OS | Test job ID | Artifact ID | Downloaded archive SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `111428030883` | `11301672862` | `d22c072762654bebcd05474873e8898eda7d7b1778cc8a3f0b4cc4b5fb58a586` |
| Linux | `111428030760` | `11302702340` | `2f671ef9049975b5b225c6227606866aaf3fb703c4af477c8876318099f1772c` |
| macOS | `111428030775` | `11301872384` | `0d8d08a4328624c42ba6676df2bf9b828e3551ef729c61bffd5c891209b77478` |

All 21 added cases were individually matched in every OS archive. The case
counts below apply independently to Windows, macOS, and Linux; every outcome
is `Passed`:

| Named test | Cases per OS |
| --- | ---: |
| `ReceivingIsExplicitViewOnlyAndPreparationShowsNoPixels` | 1 |
| `DrivingAndARequestFromAnEarlierEnableEpochAreRejected` | 1 |
| `AFailedRenderStillClearsThePreviouslyDisplayedPixelsOnDisposal` | 1 |
| `ANotificationFailureCannotPreventRealStopOrLeaveItsTaskPending` | 2 |
| `StoppingAnUnstartedUiCopyReleasesItsBorrowWithoutPresentingItLater` | 1 |
| `AnActiveRenderNotificationCanStopItsOwnerAndExternalStopStillJoins` | 1 |
| `OneFailedUiClearStillRetriesClearingPixelsAndPreservesTheFailure` | 1 |
| `PreparationWaitsForUiReadinessAndStopCancelsTheHiddenProbe` | 1 |
| `FatalStopFailureEscapesAsTheOriginalOutOfMemoryAfterOtherCleanupFails` | 2 |
| `ReceivingControlsExplainViewOnlyExecutionAndAreUnavailableByDefault` | 1 |
| `ReceivingCanBeEnabledAndStoppedWithTheKeyboard` | 1 |
| `ClosingFromAViewerNotificationStillWaitsForReceivingCleanup` | 1 |
| `DelayedPreparationChildStopJoinsRendererAndConnectionAfterPreparationReturns` | 1 |
| `PreparationCallbackStopRetainsLateRendererUntilExternalStopDrains` | 1 |
| `ViewOnlyViewerCopiesAuthenticatedTcpPixelsAndStopDrainsBothNodes` | 1 |
| `LocalStopBetweenPolicyAndGenerationReservationRejectsTheOldPreparation` | 1 |
| `ExternalConnectionWaitJoinsManagedRegistrationAfterLeaseDisposal` | 2 |
| `ConnectionWaitRemainsBoundToOriginalRegistrationWhenPeerReconnects` | 1 |

This is 11 Viewer view-model cases, three headless shell/keyboard cases, two
participant Stop-join cases, one authenticated Viewer/TCP case, one stale
receiving-epoch preparation case, and three Transport completion-join cases.
The managed two-node tracer class now contains 43 cases, all `Passed` per OS;
the new Viewer/TCP case is one additional row beyond its prior 42.

The pixel fixture explicitly uses `.UseSkia()` and
`UseHeadlessDrawing = false`. The Viewer/TCP test uses a real loopback
`TcpListener`, two Device identities, authenticated protocol 1.7 control/media,
the ViewOnly preparation peer, and the real headless Skia renderer. It observes
no pixels during Prepare, copies authenticated JPEG-decoded pixels after final
Admission, observes decoded borrowed storage cleared after rendering, and
drains both logical nodes on local Stop. Host capture, input, permission,
sharing, and Emergency boundaries in that test are recording test doubles.
These tests prove same-host TCP and headless rendering behavior, not native
host capabilities or physical two-Device operation.

### Standalone synthetic-only ABI probe

Both probe build and default run steps succeeded in each OS test job. The
committed workflow passes no `--native-self-window` argument. Direct completed-
job logs record the following actual results:

| OS | Observed default probe result |
| --- | --- |
| macOS | `.NET 10.0.12`, `Arm64`, `mode=synthetic-only`; real native ABI checks below passed. |
| Windows | `probe=skip; reason=requires_macos_14_4_or_later_ordinary_arm64; native_capture_executed=false` |
| Linux | `probe=skip; reason=requires_macos_14_4_or_later_ordinary_arm64; native_capture_executed=false` |

The macOS runner stdout includes these exact counter/result lines:

```text
copied_blocks=pass; cycles=1000; callbacks=1000; copy_helpers=1000; dispose_helpers=1000; live_contexts=0; forced_GC_before_invoke=true
objc_output_callback=pass; native_dispatch_thread=true; protocol_pre_registered=False; protocol_encoding=v40@0:8@16^{opaqueCMSampleBuffer=}24q32; synthetic_sample_cycles=1000; retained_after_producer_release=pass; bounded_row_copy=pass; live_retained_samples=0
own_window_capture=skip; reason=explicit_native_self_window_flag_required; preflight_called=false; AppKit_initialized=false; native_capture_executed=false
user_existing_window_capture_calls=0; window_title_reads=0; permissions_requested=0; pixel_files_written=0; all_block_GCHandle_contexts=0
```

Its SCStreamConfiguration, CMTime, and CGRect round trips also passed. The
Blocks, dispatch callbacks, Objective-C output callback, and synthetic sample
retains execute against real macOS frameworks. No native window capture,
permission prompt, AppKit initialization, or existing-user-window access is
claimed. Windows/Linux's successful workflow steps are explicit platform
skips, not ABI or native capture passes.

### Security and unsigned packages

Secret Scan job `111428030688` succeeded. Downloaded artifact `11302686946`,
SHA-256 `4bf27be1d770f1b3ccc207bf667d520fe5908210983ccfb0834bdd6765185fa9`,
matches its API digest and contains SARIF 2.1.0 with 208 Gitleaks rules and zero
results. Its metadata binds it to the exact verification SHA and CI run.

CodeQL job `111428062331` succeeded. Exact-SHA analysis `1888561199`, created
at `2026-10-04T11:45:08Z`, reports CodeQL 2.27.1, 52 rules, and zero results,
with empty `error` and `warning` metadata fields. Its downloaded REST SARIF
contains zero results and the exact verification revision in provenance; an
exact-SHA open-alert query returned `[]`. The log reports **391/392 C# files**
scanned and does not identify the omitted file. Full-file coverage is not
claimed. It also records three raw diagnostic messages and zero summary
diagnostics. The rule count comes from analysis metadata because the REST
SARIF omits the driver rules and invocations arrays; zero raw diagnostics is
not claimed.

All three unsigned package jobs succeeded with version `0.1.234`, build
version `0.2.34`, and `SOURCE_DATE_EPOCH=1791113969`, matching the verification
commit's timestamp. Each log records two successful seals, two
`Release package verification passed.` messages, and a successful
`diff --recursive --brief artifacts/package-1 artifacts/package-2` under
`bash -e -o pipefail`, with no difference output. Packaged composition
validation explicitly reports TEST MODE per runtime; the 26-project
direct/transitive dependency audit reports no known vulnerabilities.

| Runtime | Package job ID | Artifact ID | API/upload-log archive SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `111428709878` | `11301997325` | `8fa88a0e64ec5242dfc1c2ac173f3cbe6691bc9b7a9903aa77c77bab99eee3a2` |
| `win-x64` | `111428709843` | `11302805183` | `e1463da77d4e38d2bd682fa09fb4ae0f435900a512442ad1f268c8bf076f03c7` |
| `osx-arm64` | `111428709885` | `11302298227` | `94e1786858e3c0b75d695548cb3fe503c8f7d50493d2240346428bf44be9d19e` |

Package artifact metadata binds every archive to the exact verification SHA
and CI run, and the IDs, sizes, and digests match their upload logs. These are
GitHub artifact archive digests; package bytes were not downloaded or locally
rehashed during this audit. The two seals share one stage from one publish,
so this demonstrates repeatable packaging, not two independent reproducible
compilations. All packages are unsigned test artifacts.

Retrieve the retained hosted evidence and completed-job probe output with:

```bash
gh run view 37199481975 --repo happys2333/flowspan --json headSha,status,conclusion,number,attempt,jobs
gh run view 37199481994 --repo happys2333/flowspan --json headSha,status,conclusion,number,attempt,jobs
gh run download 37199481975 --repo happys2333/flowspan --name test-results-Windows --name test-results-Linux --name test-results-macOS --name gitleaks-results.sarif
gh api repos/happys2333/flowspan/actions/jobs/111428030775/logs
gh api repos/happys2333/flowspan/actions/jobs/111428030883/logs
gh api repos/happys2333/flowspan/actions/jobs/111428030760/logs
gh api repos/happys2333/flowspan/code-scanning/analyses/1888561199
gh api repos/happys2333/flowspan/code-scanning/analyses/1888561199 -H 'Accept: application/sarif+json'
gh api --paginate 'repos/happys2333/flowspan/code-scanning/alerts?state=open&ref=f598d920d97f9909b5dc73856996d3a5612c94c4&per_page=100'
```

## Proof boundaries

Viewer tests must distinguish real Avalonia headless Skia presentation and
authenticated TCP between two logical nodes on one host from native host
capture, input, protection, Emergency Stop, and physical two-Device behavior.
The standalone probe runs without `--native-self-window`: on supported arm64
macOS it exercises real native ABI ownership with synthetic Blocks and
CoreMedia samples, while Windows/Linux explicitly skip. It performs no native
window capture by default. Neither an ABI pass nor a platform skip is counted
as a native capture pass.

Unsigned package validation and repeated sealing do not prove signing,
notarization, or release acceptance. Headless keyboard/resource assertions do
not prove packaged native accessibility. Injected OOM does not exercise actual
allocator exhaustion. Task 5.5b status is tracked separately in the
[native Remote Window tasks](../../specs/v1/native-remote-window/tasks.md).
Native host, physical-Device, and release acceptance remain open.
