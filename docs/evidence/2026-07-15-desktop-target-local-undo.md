# Evidence: Desktop Target-Local Replace Undo, 2026-07-15

## Scope

This evidence covers task 7.3c.6 for the descriptor-complete
`workspace.note/v1` tracer. It proves one explicit target-local undo action for
an exact unexpired and unconsumed Replace capsule, including restart reduction,
durable pending/terminal outcomes, exact confirmation, and fail-closed direct
service calls.

The implementation commit is:

```text
e076bd24af631bd2f16f1f59531aa6adb2a1d90b
```

This slice does not expose the private local `ReplaceEndpoint` to an
authenticated session, does not compose a production `IReplacePeer`, and does
not add source-side `ReplaceAsync`. Destructive Replace therefore remains
locked.

## Local environment

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

## Commands

```sh
dotnet restore Flowspan.slnx --locked-mode --nologo
dotnet format Flowspan.slnx --no-restore --verify-no-changes --verbosity minimal
dotnet build Flowspan.slnx --no-restore --configuration Release --nologo
dotnet test Flowspan.slnx --no-restore --no-build \
  --configuration Release --nologo
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Two independent groups also ran in 20 fresh testhost processes per command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~RestartPlan|FullyQualifiedName~CompletedUndoReplaysAcrossStateStoreRestartWithoutRestore'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~TargetLocalUndo|FullyQualifiedName~ReplaceRecovery|FullyQualifiedName~StartupReconstructsExactSemanticReplacement|FullyQualifiedName~PendingBoundary|FullyQualifiedName~ExpiredCapsule|FullyQualifiedName~KnownStaleCapsule|FullyQualifiedName~UnknownCapsule'
```

## Local results

- locked restore passed for all 24 projects;
- formatting and patch-whitespace checks passed;
- the exact implementation commit built with 0 warnings and 0 errors;
- 568 tests passed, 0 failed, and 0 skipped:
  - Desktop: 148;
  - Transport: 137;
  - Integration: 77;
  - Security: 90;
  - Domain: 33;
  - Protocol: 17;
  - platform contracts: 10;
  - Windows platform contracts: 14;
  - macOS platform contracts: 12;
  - Linux platform contracts: 16;
  - mDNS transport contracts: 14;
- restart/replay fresh-process stress passed 20/20;
- Desktop target-local Undo fresh-process stress passed 20/20;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- NuGet reported no known vulnerable direct or transitive package in all 24
  projects; and
- the deterministic simulator negotiated protocol 1.0 and printed
  `Source preserved: True` and `Target resumed: True`. It remains a Handoff
  simulator and is not counted as target-local Undo evidence.

## Proven behavior

### Exact restart reduction

- `PersistentReplaceStateStore.GetRestartRecoveryPlan` reduces committed
  Replace and committed Undo records into Activity state transitions.
- A committed Replace consumes its captured original instance and produces the
  exact replacement. A committed Undo consumes that replacement and produces
  the preserved descriptor at `replacement revision + 1`.
- Single replacements, chained replacements, and committed undos reconstruct
  only unambiguous graph frontiers. Replay after reconstructing a process returns
  the recorded result with zero additional Adapter restore calls.
- Any pending or `Recovering` Replace/Undo globally suppresses reconstruction
  and every action. Conflicting edges, duplicate current Activity IDs,
  receipt/capsule mismatch, orphaned capsules, committed receipts without their
  capsules, and committed undos without their capsules fail closed.
- Startup admits only the supported `workspace.note/v1` semantic descriptor.
  Adapter validation or an inexact catalog add disables Replace recovery without
  blocking normal note, Handoff, or Move work.

### Target-local undo boundary

- Desktop owns one private local `ReplaceEndpoint` beside the protected state
  and live catalog. The authenticated session handler still reports no Replace
  endpoint, and `IsDestructiveReplaceAvailable` remains false.
- `UndoableCapsuleIds` is the intersection of a committed, unexpired,
  unconsumed, unattempted recovery record, the restart plan's exact candidate,
  and the live catalog's exact replacement instance.
- The Desktop service repeats that live eligibility check. A direct caller
  cannot journal through a global unresolved boundary, unknown capsule, or an
  otherwise non-actionable exact-current capsule.
- Known expired, consumed, and catalog-stale capsules retain the precise
  `UndoCapsuleExpired`, `UndoCapsuleConsumed`, and `RevisionConflict` reason.
  None crosses the Adapter restore boundary.
- Undo uses the existing pending-before-Adapter, exact-current,
  terminal-completion, single-consume, and idempotent replay invariants. No
  second undo implementation or history store exists.

### Visible confirmation and outcomes

- The recovery list exposes an action only on the exact eligible committed
  Replace record. Selection is keyboard-operable.
- Confirmation names the opaque capsule, original and replacement Activity IDs,
  and exact expiry. A selection or recovery refresh revokes confirmation.
- While the application port is pending, the duplicate action is disabled and
  the UI says not to retry. Committed, rejected, failed, and recovering results
  are projected from the recorded result rather than inferred from timeouts or
  exceptions.
- Headless Avalonia tests activate selection, confirmation, and Undo by keyboard
  and verify the automation names. The global indicator remains `NOT SHARING`.
- Projection tests prove descriptor titles, payloads, and descriptor digests
  are absent; UI canaries prove those secrets and exception text do not enter
  recovery or confirmation strings. Request digests are not fields in either
  read model.

## Hosted exact-commit evidence

[CI run 29424737663](https://github.com/happys2333/flowspan/actions/runs/29424737663)
ran from the exact implementation SHA above and completed successfully from
2026-07-15T14:42:11Z through 2026-07-15T14:45:16Z.

| Job | Job ID | Result | Exact observations |
| --- | ---: | --- | --- |
| Ubuntu | 87384104920 | success | 568 tests; locked restore, format, warning-as-error build, TEST MODE composition, simulator, and artifact upload passed |
| macOS | 87384104997 | success | 568 tests; locked restore, format, warning-as-error build, TEST MODE composition, simulator, and artifact upload passed |
| Windows | 87384105048 | success | 568 tests; locked restore, format, warning-as-error build, TEST MODE composition, simulator, and artifact upload passed |
| Secret Scan | 87384104946 | success | full-history checkout and committed-content gitleaks scan passed |

The per-job logs report the same suite counts as the local run: 148 Desktop,
137 Transport, 77 Integration, 90 Security, 33 Domain, 17 Protocol, 10 platform,
14 Windows, 12 macOS, 16 Linux, and 14 mDNS tests. Every hosted composition
validator printed the explicit TEST MODE success line; every simulator printed
`Source preserved: True` and `Target resumed: True`.

[CodeQL run 29424737971](https://github.com/happys2333/flowspan/actions/runs/29424737971)
also analyzed the exact implementation SHA. C# job `87384105976` completed
successfully from 2026-07-15T14:42:15Z through 2026-07-15T14:44:47Z, including
locked restore, analyzed-source build, and analysis.

## Explicit limits

- Hosted Windows/macOS/Linux jobs prove portable build and deterministic
  contracts, not physical two-device LAN operation.
- The local host did not execute Windows DPAPI or Linux Secret Service native
  operations. Existing platform contracts and hosted builds do not replace
  real-machine verification.
- Headless Avalonia keyboard and automation-name tests are not native screen
  reader, focus-visual, scaling, contrast, or reduced-motion evidence.
- Restart reduction reconstructs the descriptor-complete semantic note needed
  during this bounded tracer. It is not a general Activity database and does not
  recover arbitrary application process memory or unsaved external state.
- No crash/power-loss test proves every filesystem or credential-store boundary.
- Destructive source-side Replace, production target composition, physical
  recovery, and native accessibility remain open in task 7.3c.7 and the release
  criteria. Task 7.3c, parent task 7.3, and v1 are not complete.
