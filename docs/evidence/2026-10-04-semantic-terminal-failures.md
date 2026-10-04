# Semantic terminal failure ordering

Date: 2026-10-04. Branch: `codex/v1-foundation`.
Implementation: `7e7efdc91c5ca3719da20e93c7e55c61b6eeb859`.
Local environment: macOS 27.0.1 arm64, build 26A434, .NET SDK 10.0.301.

This implements Native Remote Window Task 5.5a.3f under ADR 0028 and
NR8.12–NR8.16. Exact-SHA hosted verification below completes this managed
checkpoint; the enclosing native-runtime and release gates remain open.

## Behavior

The coordinator projects non-fatal failures from four ordered slots: Primary,
Confirmation, WatchdogRelease, and OwnerCleanup. Each slot retains its original
leaf exception instances and stored order; the cached aggregate snapshot is flat.
The first OOM committed under the failure gate is retained independently of slot
order, including OOM found in a nested aggregate or ordinary inner exception.
Completed public Stop/Dispose tasks retain their original failure snapshots.
The same generation's real cleanup result is still recorded at most once.

A fallback Stop returning `FullyStopped == false` is now recorded even when the
initial Stop also returned false. If both are unconfirmed, a pending Stop and
later Dispose expose an ordered aggregate containing both boundary results.
An initial unconfirmed result followed by successful fallback retains its prior
behavior. This checkpoint adds no new public product API.

## Regression and local evidence

Two failures were reproduced before their respective fixes:

- Watchdog setup failed while initial Stop was held by a controlled barrier.
  After the public watchdog failure completed, initial Stop threw original P.
  Dispose projected `[watchdog_unavailable, P]`; the expected `[P,
  watchdog_unavailable]` failed at the first leaf identity assertion.
- Initial and fallback Stop reported different unconfirmed capture reasons.
  The expected combined failure assertion failed because Stop returned without
  throwing and silently omitted the fallback result.

Four new cases then passed in the final Debug and Release solution runs:

| Case | Observed contract |
| --- | --- |
| `StopPrimaryFailurePrecedesEarlierWatchdogFailureWithoutChangingCompletedStop` | Late Primary precedes earlier Confirmation; completed Stop keeps the original watchdog failure; fallback and owners drain. |
| `LateStopAndCleanupFailuresUseSemanticOrderAndKeepPublicTasksStable` | Exact original leaves project as `[primary, timeout, release, fallback, owner]`; nested release/owner aggregates flatten; joined Dispose tasks and completed Stop retain timeout; no duplicate owner result. |
| `EarlierConfirmationFatalDominatesLatePrimaryFatalDespiteSemanticOrdering` | Nested watchdog OOM submitted first remains the exact fatal instance after later Primary OOM; fallback and later owners complete. |
| `DifferentUnconfirmedInitialAndFallbackStopsAreBothReported` | Both distinct native boundary results remain visible through Stop and Dispose. |

The existing cancellation regression remains green: the initial Stop receives
the exact caller token, fallback receives `CancellationToken.None`, and the
original cancellation remains observable after complete cleanup. The existing
`UnconfirmedExplicitStopBlocksRestart` now checks both unconfirmed attempts.

Both warning-as-error solution builds completed with zero warnings and errors.
Each final solution configuration produced 12 TRX files with 2603 total,
executed, and passed; failed, error, timeout, aborted, and notExecuted counters
were zero. All four new named cases have `Passed` outcomes in both sets of TRX.
Desktop passed 739/739 per configuration. The focused Debug coordinator run
passed 134/134. Format verification, diff checks, explicit TEST MODE composition,
protocol-1.7 simulator, and direct/transitive NuGet vulnerability audit passed.
One read-only scope review returned APPROVE with no P0/P1/P2 defects.

Reproduce, substituting Debug for Release for both configurations:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore --logger trx --results-directory TestResults --blame-hang-timeout 3m
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj --configuration Release --no-build --no-restore -- --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive
git diff --check
```

Local TRX: `/tmp/flowspan-2026-10-04/solution-debug` and
`/tmp/flowspan-2026-10-04/solution-release`. These temporary files are not durable
repository artifacts. Independently downloaded hosted results are recorded below.

## Hosted verification

[CI 37195202126](https://github.com/happys2333/flowspan/actions/runs/37195202126)
and [CodeQL 37195202142](https://github.com/happys2333/flowspan/actions/runs/37195202142)
are run 230, attempt 1, completed successfully at the exact implementation SHA
`7e7efdc91c5ca3719da20e93c7e55c61b6eeb859`.

All three OS test artifacts were downloaded. Their archive bytes match the
API digests below; each artifact's metadata binds it to this CI run and exact
implementation SHA. Each archive contains 12 parsed TRX files with 2603 total,
executed, and passed tests, and all remaining counters zero (`failed`, `error`,
`timeout`, `aborted`, `inconclusive`, `passedButRunAborted`, `notRunnable`,
`notExecuted`, `disconnected`, `warning`, `completed`, `inProgress`, and
`pending`). Every `UnitTestResult` has outcome `Passed`. Desktop is separately
739/739 total, executed, and passed on every OS, with its other counters zero.

| OS | Test job ID | Artifact ID | Downloaded archive SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `111415512766` | `11300951255` | `613cf7fd21af8ddb931a4b8a6193406404c84e2c5c44313212ddc3c960752ff1` |
| Linux | `111415512813` | `11301095708` | `509f82416531997a6b54eeac62ad9af89a598935ec1cf75033b825fce645cb5a` |
| macOS | `111415512836` | `11301020989` | `4cad4dd3e88c2d533faa547fbf05f28d1cdaf6794dc9f5c55bdc86dd7312124e` |

Each of the four new named cases in the regression table has exactly one
`Passed` result in each OS archive. The existing
`StopFirstCallerCancellationRunsOneFallbackAndPreservesTheExactToken` and
`UnconfirmedExplicitStopBlocksRestart` each also have exactly one `Passed`
result per OS. The ten cases from
[the watchdog checkpoint](2026-09-08-watchdog-failures.md) were individually
matched in the same TRX: direct/nested setup OOM has four cases, ordinary setup
failure two, late callback one, release failure after timeout two, and the
cross-thread confirmation-operation contract one; every outcome is `Passed`.
This supplies the named-case evidence that the expired historical run 229
artifacts could no longer provide.

Secret Scan job `111415512666` succeeded. Downloaded artifact `11300104865`,
SHA-256 `240d5e8f949309417a4e2fe8a33983fe0a62ab9b68765bdf2cf4053e78882aed`,
matches its API digest and contains SARIF 2.1.0 with 208 Gitleaks rules and zero
results. Its metadata binds the archive to the same exact implementation SHA
and CI run. This is the hosted committed-content scan; no local Gitleaks run
is claimed.

CodeQL job `111415512615` succeeded. Exact-SHA analysis `1888412158`, created
at `2026-10-04T10:27:21Z`, reports CodeQL 2.27.1, 52 rules, and zero results,
with empty analysis `error` and `warning` fields. Its downloaded REST SARIF
contains zero results and the exact implementation revision in
`versionControlProvenance`. The exact-SHA open-alert query returned `[]`.
The rule count comes from analysis metadata; the REST SARIF omits the driver
rules and invocations arrays. The job log records three raw diagnostic
messages, so zero raw diagnostics is not claimed.

All three unsigned package jobs succeeded with version `0.1.230`, build
version `0.2.30`, and `SOURCE_DATE_EPOCH=1791109392`, equal to the implementation
commit's timestamp. Each job log records two successful seals, two
`Release package verification passed.` messages, and a successful
`diff --recursive --brief artifacts/package-1 artifacts/package-2` step under
`bash -e -o pipefail`, with no difference output. Both seals share one
`artifacts/stage` created by one publish operation. This proves repeatable
packaging of the stage, not two independent reproducible compilations.
Packaged composition validation explicitly reports TEST MODE on every
runtime. The direct/transitive dependency audit passed with no known
vulnerabilities reported by any of its 26 projects.

| Runtime | Package job ID | Artifact ID | API/upload-log archive SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `111416267602` | `11301140944` | `174f9c5aa714ff1fa4a9fcfaf227ace01e8f596a961dc4b5bcf98408209f8716` |
| `win-x64` | `111416267640` | `11301470226` | `5b93b33681d0a75176da8aacccafabc091c1d9567365e0011eb74f4a06d88417` |
| `osx-arm64` | `111416267582` | `11300339642` | `67f305d86c7c1a11088df5239fc16a578cd3992a34b94b1254b1891867babc3a` |

The package artifact metadata binds each archive to the exact implementation
SHA and CI run, and its ID, size, and digest match the upload logs. These
digests identify GitHub artifact archives, not their inner release packages.

Reproduce the hosted state and retrieve the retained evidence:

```bash
gh run view 37195202126 --repo happys2333/flowspan --json headSha,status,conclusion,number,attempt,jobs
gh run view 37195202142 --repo happys2333/flowspan --json headSha,status,conclusion,number,attempt,jobs
gh run download 37195202126 --repo happys2333/flowspan --name test-results-Windows --name test-results-Linux --name test-results-macOS --name gitleaks-results.sarif
gh api repos/happys2333/flowspan/actions/runs/37195202126/artifacts
gh api repos/happys2333/flowspan/code-scanning/analyses/1888412158
gh api repos/happys2333/flowspan/code-scanning/analyses/1888412158 -H 'Accept: application/sarif+json'
gh api --paginate 'repos/happys2333/flowspan/code-scanning/alerts?state=open&ref=7e7efdc91c5ca3719da20e93c7e55c61b6eeb859&per_page=100'
gh run view 37195202126 --repo happys2333/flowspan --log
```

## Limits

This is portable coordinator evidence for one active generation and an
uncontended lifecycle gate. It adds no production-composed tracer row, native
API, physical two-Device, signed package, notarization, or release evidence.
Actual allocator exhaustion is not injected; boundary-thrown OOM does not prove
continuation through every allocation failure inside diagnostic construction.
Cancellation combined with terminal observers, initial-false/fallback-success
direct coverage, pre-generation cleanup, lifecycle contention, and the complete
fault matrix remain open. The production host remains unavailable and the
long-term Goal remains active.
