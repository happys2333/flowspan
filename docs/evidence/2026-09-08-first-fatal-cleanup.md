# First fatal cleanup failure

Evidence audited: 2026-09-08. Implementation and hosted runs: 2026-08-30.
Branch: `codex/v1-foundation`.
Implementation: `01df06e20cb966ce0449f3d63a275c32276830f6`.

## Behavior and scope

Task 5.5a.3d preserves the first fatal `OutOfMemoryException` committed under
the terminal-failure gate. Both the normal failure recorder and its
allocation-failure fallback retain that exact instance when later failures
arrive. Non-fatal flattening, cleanup ordering, and public completion are
unchanged.

`DisposeFirstTimeoutKeepsTheFirstFatalOutOfMemoryAndDrainsLaterOwners` covers
one stable active generation, external Dispose-first initiation, an
uncontended lifecycle gate, and a manual ten-second watchdog. An authenticated
Connection disposal remains blocked at deadline minus one tick and exact
equality; equality publishes the shared public `host_cleanup_timeout`.
Releasing the Connection throws direct OOM B. During completion processing,
the watchdog is physically removed before its disposal hook throws nested
OOM A. Watchdog-release failure A is committed before the owner-cleanup result
B, so A remains the terminal diagnostic. “First” means first committed to the
ledger, not the wall-clock order in which owners throw.

The regression proves exact A identity, one attempt at each injected failure,
the unchanged public Dispose Task and timeout instance, zero remaining timers,
empty media budget, completed independently safe owner cleanup, and cleared
retiring ownership. It does not inject a real allocation failure inside the
failure recorder; the fallback guard is supported by code review, not a claim
that this test executes that catch path.

Before the fix, the focused test expected exact watchdog OOM A but observed
exact Connection OOM B (RED). The final implementation passed (GREEN). Four
read-only reviews reported APPROVE with zero P0, P1, or P2 findings.

## Local verification

Recorded macOS managed-contract results for the implementation:

| Scope | Debug | Release |
| --- | ---: | ---: |
| Focused regression | 1/1 | 1/1 |
| Twenty fresh focused processes | 20/20 | 20/20 |
| Coordinator class | 121/121 | 121/121 |
| Desktop project | 725/725 | 725/725 |
| Solution | 2589/2589 | 2589/2589 |

Both warning-as-error solution builds had zero warnings and errors. Formatting,
diff checks, explicit TEST MODE composition, deterministic simulator, and the
direct/transitive NuGet vulnerability audit passed. These are the recorded
implementation checks; the 2026-09-08 audit below rechecks hosted evidence.

Reproduce the focused row and full solution, substituting `Debug` for `Release`
to verify both configurations:

```bash
dotnet restore Flowspan.slnx --locked-mode
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~DisposeFirstTimeoutKeepsTheFirstFatalOutOfMemoryAndDrainsLaterOwners'
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

## Hosted verification

[CI 33320452092](https://github.com/happys2333/flowspan/actions/runs/33320452092)
and [CodeQL 33320452105](https://github.com/happys2333/flowspan/actions/runs/33320452105)
are both run 228, attempt 1, completed successfully at the exact implementation
SHA above. CI runs the solution in Release.

Each downloaded OS artifact contains 12 TRX files and the named regression
with outcome `Passed`. Aggregated counters are 2589 total, executed, and passed;
all other counters are zero (`failed`, `error`, `timeout`, `aborted`,
`inconclusive`, `passedButRunAborted`, `notRunnable`, `notExecuted`,
`disconnected`, `warning`, `completed`, `inProgress`, and `pending`).

| OS | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| Windows | `99281374766` | `9734783997` | `acbdf6fc0198e62ada1064a09d6276be99424b0903c2fc0e3aa2c2ed842b31b5` |
| macOS | `99281374723` | `9734760866` | `ee42474379a8173496ad42d9af518c3cf61982491f9471faef00f2906e7b26ed` |
| Linux | `99281374639` | `9734771563` | `c013eda486d91cba7d72f95400dbb59e30c3613e6af32692502ab502fe575d4f` |

Secret Scan job `99281374791` succeeded. Downloaded artifact `9734727189`,
SHA-256 `576fecfc884aef7e10e6c5f0956aa0e4307bf70885aff6fb31763e07cfaed81c`,
contains SARIF 2.1.0 with 208 Gitleaks rules and zero results. No local Gitleaks
execution is claimed.

CodeQL job `99281374996` succeeded. Exact-SHA analysis `1694427742` used
CodeQL 2.26.4, evaluated 52 rules, and reported zero results, errors, and
warnings. Querying open alerts with the exact commit as `ref` returned zero.

All three unsigned package jobs used version `0.1.228` (build version
`0.2.28`, `SOURCE_DATE_EPOCH=1788104611`). Their logs show two successful seals,
two `Release package verification passed.` messages, and a successful
`diff --recursive --brief artifacts/package-1 artifacts/package-2` under a
fail-fast shell. This verifies repeatable packaging of one publish stage; it
does not establish two independent reproducible compilations.

| Runtime | Job ID | Artifact ID | Artifact SHA-256 |
| --- | ---: | ---: | --- |
| `linux-x64` | `99281956635` | `9734804473` | `aaef13c3c89a1633986a16702840d9f62873b1cde3c68511a15e7f9693a525e3` |
| `osx-arm64` | `99281956649` | `9734807727` | `5df201db91b326cc42ebbac3911644980dd0d380d1cce12b3990015a7c271616` |
| `win-x64` | `99281956651` | `9734809526` | `8eecaf45eb9340a3345a753191e568917addb9ce3e95ae63289a6c051a6a5acf` |

These digests identify GitHub artifact archives, not their inner release
packages. Artifact metadata binds every artifact to the exact implementation
SHA and CI run. Downloaded test and Secret Scan archive bytes hash to the
listed digests; package digests also match the upload logs.

## Acceptance impact

Only Task 5.5a.3d is completed. No boundary-matrix cell changes status and the
production-composed managed tracer remains 42 cases. Other timer faults,
initiators and owner combinations, lifecycle contention, cleanup-completion
wins, and pre-generation cleanup remain open.

This is managed/contract evidence on local macOS and hosted Windows, macOS,
and Linux. It does not prove native capture/input/protection/permission/
Emergency APIs, physical two-Device use, packaged accessibility, signing,
notarization, or release acceptance. Tasks 5, 5.5a.3, 5.5a, 5.5, all later
native/physical/release gates, and the long-term Goal remain open.
`CreateProduction()` continues to report Remote Window unavailable.
