# Watchdog provider failures and completion ordering

Evidence audited: 2026-10-04. Implementation and hosted runs: 2026-09-08.
Branch: `codex/v1-foundation`.
Implementation commit: `972d95749447810004972c6a1ec645712a40651b`.
Local environment: macOS 26.6.2 arm64, build 25G83, .NET SDK 10.0.301.

This is the Task 5.5a.3e managed-contract checkpoint under
[ADR 0028](../adr/0028-bounded-remote-window-cleanup-confirmation.md).
The original exact-commit hosted runs succeeded, although their retained TRX
artifacts are no longer available. A subsequent exact-commit checkpoint on
2026-10-04 supplies downloaded, individually matched hosted TRX for all ten
cases below.

## Implemented behavior

The coordinator retains the original timer-provider setup exception in its
internal `WatchdogSetupFailure` lane. Ordinary setup failures expose only a
bounded public reason: `watchdog_unavailable`, or `host_cleanup_timeout` when
the provider invoked the timeout callback first. Raw provider text is excluded
from that public exception and from its `TerminalFailure` projection.

Direct or nested setup `OutOfMemoryException` is retained by its original
instance in the fatal diagnostic lane, including when the provider invokes the
timeout callback before throwing. External Dispose can expose that fatal
instance if its public Task has not completed. An already completed public
Task retains its original result. A non-fatal timer-release failure after
timeout becomes an ordered late diagnostic `[timeout, release failure]`, while
the public Dispose Task and timeout instance remain unchanged.

The confirmation operation now chooses its winner under its private lock and
invokes external commit callbacks after releasing that lock. This removes the
lock cycle in which a timeout callback waits for coordinator state while
provider setup, still under that state lock, attempts to re-enter the winner
lock. When real cleanup loses, it waits for confirmation publication before
recording timer-release and owner-cleanup results, preserving diagnostic order.

## Regression evidence

Four behavioral RED results were observed during development:

| Trigger | Failure before the fix |
| --- | --- |
| Nested provider setup OOM | The original fatal exception became a bounded watchdog failure. |
| Ordinary setup exception | The original setup diagnostic was absent (`null`). |
| Provider callback followed by setup OOM | The fatal exception was lost behind the earlier timeout winner. |
| Cross-thread timeout commit held while provider throws | Start exceeded the test's five-second bound; releasing the test barrier in `finally` allowed it to finish. |

A separate mutation removed the losing cleanup path's
`await completion.Task`. The operation test then failed at
`Assert.False(realCommitted.Task.IsCompleted)` with actual `true`. The source
was restored. This verifies that the ordering assertion detects early owner
recording rather than passing because its continuation has not been scheduled.

The added cases comprise nine coordinator cases and one confirmation-operation
case:

| Test | Cases | Direct observation |
| --- | ---: | --- |
| `WatchdogCreationOutOfMemoryEscapesUnchangedAndCleanupStillDrains` | 4 | Direct/nested OOM, each with/without callback-before-throw; exact fatal and raw setup identities, fatal public result, retained cleanup ownership and drain. |
| `WatchdogCreationFailureKeepsPrivateDiagnosticsAndBoundsPublicFailure` | 2 | Callback-before-throw on/off; exact private exception, public reason and absence of provider canary text, stable public Task after drain. |
| `WatchdogLateCallbackCannotReplaceCompletedCleanup` | 1 | Cleanup wins, timer releases, repeated stale callbacks cannot publish timeout or repeat cleanup. |
| `WatchdogReleaseFailureAfterTimeoutPreservesPublicCompletion` | 2 | Timeout after timer return or during timer creation; exact late failure order, timer release and immutable public result. |
| `ProviderFailureDoesNotWaitForAnInFlightTimeoutCommit` | 1 | Provider Start returns while the cross-thread commit is held; late owner recording waits for confirmation publication. |

The operation case uses a provider that first waits for the timeout commit
barrier, completes real cleanup, and then throws. Start subsequently installs
its observer against an already-completed cleanup Task. Its return therefore
proves that the observer has reached the next await; the ordering assertion
does not depend on continuation scheduling or the test runner's synchronization
context. The `finally` path releases the commit barrier, supplies a fallback
cleanup result, and waits for both Start and the callback before disposing the
test events.

## Final local verification

This section records the implementation's 2026-09-08 local validation; the
2026-10-04 hosted evidence audit did not repeat these unchanged-code runs.

The final implementation passed 2599/2599 solution tests and 735/735 Desktop
tests in both Debug and Release. These full runs include all ten new cases
after the final test assertions were tightened. Both warning-as-error builds
had zero warnings and errors. Formatting, diff checks, explicit TEST MODE
composition, deterministic simulator, and direct/transitive NuGet vulnerability
audit all passed.

An earlier focused run passed 11/11, comprising the ten added cases and one
existing case. It preceded the final assertion changes; no separate focused
rerun of the final assertions is claimed. The final full runs provide that
validation.

## Hosted verification

[CI 34177668586](https://github.com/happys2333/flowspan/actions/runs/34177668586)
and [CodeQL 34177668498](https://github.com/happys2333/flowspan/actions/runs/34177668498)
are run 229, attempt 1, completed successfully at the exact implementation SHA
above. CI completed at `2026-09-08T01:51:52Z`; CodeQL completed at
`2026-09-08T01:50:26Z`. The 2026-10-04 audit reads the final run/job state and
the retained job logs, replacing the earlier in-progress handoff snapshot.

Each OS test job ran the unfiltered Release solution command:

```bash
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore --logger "trx;LogFilePrefix=<runner.os>" --results-directory TestResults --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
```

Each retained log contains 12 assembly result summaries and 12 `Results File`
lines. Aggregating the summaries yields 2599 total and passed, zero failed,
and zero skipped on each OS. The Desktop assembly separately reports
735/735 passed, zero failed, and zero skipped on each OS. Formatting,
warning-as-error build, explicit TEST MODE composition, and deterministic
simulator steps also succeeded on all three OS jobs.

| OS | Test job ID | Solution totals from log | Desktop totals from log |
| --- | ---: | --- | --- |
| Windows | `101910210145` | 2599/2599 passed; 0 failed/skipped | 735/735 passed; 0 failed/skipped |
| Linux | `101910210179` | 2599/2599 passed; 0 failed/skipped | 735/735 passed; 0 failed/skipped |
| macOS | `101910210186` | 2599/2599 passed; 0 failed/skipped | 735/735 passed; 0 failed/skipped |

The logs record successful upload of the following test archives. These are
upload-log digests, not archive bytes downloaded during this audit:

| Artifact | ID | Upload-log SHA-256 |
| --- | ---: | --- |
| `test-results-Windows` | `10037883452` | `61e2fb2ef5dd53ff2bd215d158c171c73954bc2c950859cc4de68a8b8ccff088` |
| `test-results-Linux` | `10037859068` | `11c82733d17f555ed58a1757588f1249012d2c2a6cffb9881e59badf5bbece74` |
| `test-results-macOS` | `10037853905` | `f7b932949fc29a285649dde4de1faba7bf7baeb80e5ddec64331a5046afd2554` |

The upload logs specify `retention-days: 14`. On 2026-10-04, all three
archives were absent from the run's artifact listing and their individual
artifact API endpoints returned HTTP 404. No corresponding hosted TRX cache
was found in the task's temporary directories. The logs do not print the ten
new cases' individual names. Consequently this audit proves full-suite and
Desktop aggregate success, but does not claim downloaded/parsed TRX,
individual hosted outcomes for those ten cases, or the additional TRX-only
non-success counters. A later checkpoint with an exact implementation SHA
and retained named-case TRX can supply that remaining evidence.

Secret Scan job `101910210068` succeeded. Downloaded artifact `10037807003`
contains SARIF 2.1.0 with 208 Gitleaks rules and zero results. It was downloaded
again during this audit; the archive's SHA-256 matches
`cfb024210d4b2954197ff34d45a885d8c80231316da5bd72ea4db42c82f04264`.

The current artifact metadata binds this archive to the implementation SHA
and CI run. Gitleaks scanned the push's committed range
`5ea0833633f630efeaba84c6aeb9cc14ea9ee4b8^..972d95749447810004972c6a1ec645712a40651b`
with `--no-merges --first-parent`. No local Gitleaks execution or full-history
scan is claimed.

CodeQL job `101910209521` succeeded. Exact-SHA analysis `1738411966`, created
at `2026-09-08T01:50:16Z`, reports CodeQL 2.26.4, 52 rules, and zero results.
Its analysis metadata has empty `error` and `warning` fields. The downloaded
REST SARIF is version 2.1.0, contains zero results, and binds its
`versionControlProvenance.revisionId` to the exact implementation SHA. An
exact-SHA open-alert query returned `[]`. The REST SARIF omits the driver
`rules` and `invocations` arrays, so the rule count comes from the analysis
metadata. The job log records three raw diagnostic messages; zero raw
diagnostics is not claimed.

All three unsigned package jobs succeeded with version `0.1.229`, build
version `0.2.29`, and `SOURCE_DATE_EPOCH=1788831878`, equal to the implementation
commit's timestamp. Each job log records two successful seals, two
`Release package verification passed.` messages, and the command
`diff --recursive --brief artifacts/package-1 artifacts/package-2` in a
successful step under `bash -e -o pipefail`. Each pair of seals shares one
`artifacts/stage` produced by one publish operation. This proves repeatable
packaging of that stage, not two independently reproducible compilations.
Packaged composition validation was explicitly TEST MODE on each runtime.

| Runtime | Package job ID | Artifact ID | Upload-log SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `101910988237` | `10037911995` | `b9599c9690ad4aa54e94f4bc2bed68c263a6231e44c9a48544933d9188351a06` |
| `win-x64` | `101910988331` | `10037921673` | `61ec5e64ee3b1f2fae47e77f9fd7738cea79e34a929aff481b8e4b887167f9c7` |
| `osx-arm64` | `101910988409` | `10037908915` | `05262262ab0fe31c2c1fbe7840950dd4d9ac4bce5a0dfa4a7e1cc5cf2da38dcd` |

These package archive digests come from upload logs and identify GitHub
artifact archives, not their inner release packages. All three package
artifacts also had 14-day retention, were absent from the artifact listing on
2026-10-04, and returned HTTP 404 from their individual artifact API endpoints.
Package bytes were not downloaded or independently rehashed during this audit.

Reproduce the retained hosted state and log checks while GitHub retains them:

```bash
gh run view 34177668586 --repo happys2333/flowspan --json headSha,status,conclusion,number,attempt,jobs
gh run view 34177668498 --repo happys2333/flowspan --json headSha,status,conclusion,number,attempt,jobs
gh run view 34177668586 --repo happys2333/flowspan --log
gh api repos/happys2333/flowspan/actions/runs/34177668586/artifacts
gh api repos/happys2333/flowspan/code-scanning/analyses/1738411966
gh api repos/happys2333/flowspan/code-scanning/analyses/1738411966 -H 'Accept: application/sarif+json'
gh api --paginate 'repos/happys2333/flowspan/code-scanning/alerts?state=open&ref=972d95749447810004972c6a1ec645712a40651b&per_page=100'
git show 972d95749447810004972c6a1ec645712a40651b:.github/workflows/ci.yml
```

## Subsequent named-case checkpoint

Implementation `7e7efdc91c5ca3719da20e93c7e55c61b6eeb859` retains all ten
watchdog cases and adds the separate semantic failure ordering slice.
[CI 37195202126](https://github.com/happys2333/flowspan/actions/runs/37195202126)
and [CodeQL 37195202142](https://github.com/happys2333/flowspan/actions/runs/37195202142)
are run 230, attempt 1, completed successfully at that exact SHA.

Downloaded Windows, Linux, and macOS archives each contain 12 parsed TRX
files, 2603 total/executed/passed tests, and 739 total/executed/passed Desktop
tests. Every other counter is zero. Each of the five test families in the
regression table was matched by name in every archive with exactly the listed
case count (4, 2, 1, 2, and 1 respectively); all ten outcomes are `Passed` on
each OS. The downloaded archive bytes match their API digests:

| OS | Artifact ID | Downloaded archive SHA-256 |
| --- | ---: | --- |
| Windows | `11300951255` | `613cf7fd21af8ddb931a4b8a6193406404c84e2c5c44313212ddc3c960752ff1` |
| Linux | `11301095708` | `509f82416531997a6b54eeac62ad9af89a598935ec1cf75033b825fce645cb5a` |
| macOS | `11301020989` | `4cad4dd3e88c2d533faa547fbf05f28d1cdaf6794dc9f5c55bdc86dd7312124e` |

Artifact metadata binds every archive to the later exact implementation SHA
and CI run. This supplies the missing named-case checkpoint evidence without
claiming that the expired historical run 229 TRX were recovered. Full
run-230 Secret Scan, CodeQL, package, and semantic-slice evidence is in
[semantic terminal failure ordering](2026-10-04-semantic-terminal-failures.md).
Task 5.5a.3e is complete with this later named-case checkpoint.

## Acceptance limits

The coordinator cases exercise managed Dispose initiation; the additional
cross-thread case tests the internal confirmation operation with controlled
callbacks. The original watchdog slice did not cover explicit Stop primary-
failure ledger ordering; the subsequent Task 5.5a.3f slice addresses that
ordering separately. Lifecycle-gate contention, pre-generation cleanup, and
the complete failure matrix remain open. This adds no production-composed
tracer case; that class remains 42.

Native APIs, physical two-Device use, packaged accessibility, signing,
notarization, and release acceptance remain unverified by this checkpoint.
Tasks 5, 5.5a.3, 5.5a, 5.5, later native/physical/release gates, and the long-term
Goal remain open. `CreateProduction()` continues to report Remote Window
unavailable.
