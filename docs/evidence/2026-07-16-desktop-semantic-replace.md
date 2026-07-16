# Evidence: Desktop Exact-Confirmation Semantic Replace, 2026-07-16

## Scope

This evidence covers task 7.3c.7 for the descriptor-complete
`workspace.note/v1` tracer. It proves that the previously private protected
target Replace endpoint is composed as a Trust-bound authenticated peer and
that Desktop exposes one exact-confirmation source command with send-time
revalidation, a payload-free receipt/capsule result, truthful uncertainty, and
source preservation.

The implementation commit is:

```text
30e2db4dc5aa5c58584a6e3b5b26fb655aa1d624
```

Task 7.3c.7 is a composition slice. It does not close task 7.3c, parent task
7.3, the Replace release criterion, or v1 because physical two-device,
native-accessibility, native-application, and crash/power-loss evidence remains
open.

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
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

Two independent filters also ran in 20 fresh testhost processes per command:

```sh
dotnet test tests/Flowspan.Integration.Tests/Flowspan.Integration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Replace'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter 'FullyQualifiedName~Replace'
```

## Local results

- locked restore, format verification, and patch-whitespace checks passed;
- the implementation built with 0 warnings and 0 errors;
- 579 tests passed, 0 failed, and 0 skipped:
  - Desktop: 158;
  - Transport: 137;
  - Integration: 78;
  - Security: 90;
  - Domain: 33;
  - Protocol: 17;
  - platform contracts: 10;
  - Windows platform contracts: 14;
  - macOS platform contracts: 12;
  - Linux platform contracts: 16;
  - mDNS transport contracts: 14;
- Integration Replace fresh-process stress passed 20/20, with 54 filtered tests
  in every process;
- Desktop Replace/runtime/recovery fresh-process stress passed 20/20, with 36
  filtered tests in every process;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- NuGet reported no known vulnerable direct or transitive package in all 24
  projects; and
- the deterministic simulator negotiated protocol 1.0 and printed
  `Source preserved: True` and `Target resumed: True`. It remains a Handoff
  simulator and is not counted as semantic Replace evidence.

## Proven behavior

### Source command and exact preflight

- Desktop accepts an incoming Activity ID plus the exact selected target
  snapshot; callers do not construct a wire command.
- The runtime requires usable protected recovery, rechecks the current
  peer-relative `activity.receive`, queries fresh purpose-scoped inventory, and
  matches device, target ID, revision, descriptor digest, kind, title, and
  placement before constructing an Operation ID.
- A preflight failure returns no destructive Operation/correlation identity.
  Send-time target mismatch is `RevisionConflict`, sends no destructive request,
  clears the stale inventory, and requires a new selection and confirmation.
- The unchanged live incoming Activity, Trust, recovery state, and authenticated
  channel are rechecked immediately before command construction. Replace never
  removes the source Activity.

### Protected target and serialization boundary

- Production composes `IReplacePeer` only when the protected Replace store and
  endpoint open successfully. A missing, corrupt, unsupported, or unreadable
  store keeps destructive Replace unavailable while leaving other Activity work
  usable.
- The target reloads the authenticated sender's peer-relative
  `activity.replace` for every destructive request. Same-session revocation is
  rejected without target mutation.
- Existing pending or `Recovering` Replace/undo state returns
  `OperationInProgress` without a new journal entry or Adapter call.
- Inbound Replace and target-local undo acquire the same target serialization
  boundary before journal creation. A deterministic concurrency test proves a
  second distinct Replace cannot enter the journal while the first owns that
  boundary; exact retries still replay the recorded result.
- A successful encrypted same-host operation stores a 15-minute target-owned
  capsule before incoming resume, returns matching receipt/capsule bindings,
  replaces the target catalog entry, publishes target state change, and leaves
  the source active.

### Visible outcomes and accessibility contracts

- Activation immediately shows `REPLACE PENDING — DUPLICATE DISABLED` and
  disables the command while the application action is pending.
- Success requires an acknowledged committed receipt and a verified non-null
  undo capsule. A committed response without a capsule is presented as invalid,
  not as success.
- Committed output exposes the payload-free Operation/correlation IDs, reason,
  timestamp, capsule ID, and exact expiry. Activity payload and exception text
  never enter these strings.
- After destructive invocation begins, cancellation, disconnect, or unexpected
  application-port failure is conservatively acknowledgement loss: the target
  may have committed, the source remains active, automatic retry is forbidden,
  and the UI directs the user to target recovery.
- An existing target recovery boundary says `DO NOT RETRY`. The target endpoint
  remains the authoritative fail-closed enforcement even if a source UI is
  stale.
- The Avalonia action has a specific automation name/help text and activates
  through the standard Space-key path. Headless tests verify result fields and
  that the global safety indicator remains `NOT SHARING`.

## Hosted exact-commit evidence

[CI run 29463732907](https://github.com/happys2333/flowspan/actions/runs/29463732907)
ran from the exact implementation SHA above and completed successfully from
2026-07-16T01:17:05Z through 2026-07-16T01:19:58Z.

| Job | Job ID | Result | Exact observations |
| --- | ---: | --- | --- |
| Windows | 87512380928 | success | 579 tests; locked restore, format, warning-as-error build, TEST MODE composition, simulator, and artifact upload passed |
| macOS | 87512380929 | success | 579 tests; locked restore, format, warning-as-error build, TEST MODE composition, simulator, and artifact upload passed |
| Ubuntu | 87512380946 | success | 579 tests; locked restore, format, warning-as-error build, TEST MODE composition, simulator, and artifact upload passed |
| Secret Scan | 87512380961 | success | full-history checkout and committed-content gitleaks scan passed |

Downloaded TRX artifacts report the same all-passed suite counts as the local
run: 158 Desktop, 137 Transport, 78 Integration, 90 Security, 33 Domain, 17
Protocol, 10 platform, 14 Windows, 12 macOS, 16 Linux, and 14 mDNS tests. No TRX
reports a failed, skipped, not-executed, or aborted test.

[CodeQL run 29463732915](https://github.com/happys2333/flowspan/actions/runs/29463732915)
also analyzed the exact implementation SHA. Analyze C# job `87512380926`
completed successfully from 2026-07-16T01:17:08Z through
2026-07-16T01:20:19Z, including locked restore, analyzed-source build, and
analysis.

## Hosted evidence-commit verification

The evidence and task-status commit
`57bde4462442bd26b1423023c31a90f5677c87dd` was independently verified by
[CI run 29464037893](https://github.com/happys2333/flowspan/actions/runs/29464037893)
from 2026-07-16T01:24:31Z through 2026-07-16T01:27:13Z:

| Job | Job ID | Result |
| --- | ---: | --- |
| Windows | 87513302480 | success |
| macOS | 87513302525 | success |
| Ubuntu | 87513302468 | success |
| Secret Scan | 87513302465 | success |

Every OS job again passed locked restore, format, warning-as-error build, all 579
tests, TEST MODE composition, deterministic simulator, and evidence upload.

[CodeQL run 29464037951](https://github.com/happys2333/flowspan/actions/runs/29464037951)
and Analyze C# job `87513302725` also completed successfully for the exact
evidence commit from 2026-07-16T01:24:35Z through 2026-07-16T01:27:52Z. Thus the
implementation and the commit recording task 7.3c.7 both passed the same hosted
gates. The closure commit that records this second result remains subject to
those gates before its status is treated as final.

## Explicit limits

- Same-host encrypted loopback and hosted runners do not prove operation between
  two physical devices over a representative LAN.
- The local host did not execute Windows DPAPI or Linux Secret Service native
  user-session lifecycles. Hosted platform contracts and API smoke do not replace
  real-machine protected-store verification.
- Headless Avalonia keyboard and automation-name tests are not native screen
  reader, focus-visual, scaling, contrast, or reduced-motion evidence.
- Only the descriptor-complete `workspace.note/v1` semantic tracer is composed.
  Flowspan does not claim migration of arbitrary application process memory,
  unsaved external state, credentials, screen media, or remote input.
- No physical crash or power-loss test proves every filesystem,
  credential-store, journal, and Adapter boundary.
- The simulator remains Handoff-only. It validates the deterministic foundation,
  not Replace or target-local recovery.
- Task 7.3c, parent task 7.3, the Replace release criterion, packaging, and v1
  remain incomplete.
