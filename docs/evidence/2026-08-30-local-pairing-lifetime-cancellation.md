# Local pairing lifetime-cancellation precedence checkpoint

Date: 2026-08-30

Branch: `codex/v1-foundation`

Production-fix commit:
`72394484e9fd0fd556497641f1ac5d79afe80bce`

Final hosted evidence tree: pending a post-fix exact workflow execution.

Local environment: macOS 26.6.2 arm64, .NET SDK 10.0.301

## Scope

This checkpoint repairs deterministic lifetime-cancellation precedence in
`DesktopLocalPairingRuntime.EnableAsync`. It is unrelated to a Remote Window
boundary-matrix status and changes no tracer count.

Dispose initiates the runtime lifetime token's asynchronous cancellation and
sets the runtime disposed state. A deliberately cancellation-ignoring session
start may return after disposal before asynchronous cancellation propagation to
the linked token has executed. In that late `startRejected` classification
window, the runtime previously checked the linked token and then the disposed
flag. It could therefore surface `ObjectDisposedException` even though lifetime
cancellation had already won the operation.

The one-line fix checks `lifetimeCancellation.Token` before the linked token and
disposed state. A lifetime cancellation now deterministically produces
`OperationCanceledException`; linked caller cancellation retains the next
precedence, while genuine disposal without either cancellation remains
`ObjectDisposedException`. Cleanup and late-session Stop behavior are unchanged.

## Hosted failure history

[CI run `33304022418`](https://github.com/happys2333/flowspan/actions/runs/33304022418),
run number 214 attempt 1, is **failure evidence**, not a successful checkpoint,
for exact documentation tree
`9ca4b2c5665cc7ffd462a1a59b8314388f16bc58`.

Only macOS job `99237248671` failed, in
`DesktopLocalPairingRuntimeTests.DisposeRejectsAndStopsCancellationIgnoringLateEnableSession`:

- expected: `OperationCanceledException`;
- actual: `ObjectDisposedException` from `DesktopLocalPairingRuntime.EnableAsync`.

Ubuntu job `99237248699`, Windows job `99237248779`, and Secret Scan job
`99237248763` succeeded. The package matrix job `99237778338` was skipped because
the required test matrix was not successful; no package result from this run is
claimed.

[CodeQL run `33304022374`](https://github.com/happys2333/flowspan/actions/runs/33304022374),
run number 214 attempt 1, independently completed with `success`. Job
`99237248324` produced analysis ID `1693740950` and SARIF ID
`7eace976-a455-11f1-9f4a-a44bc3e1e004`; service warning/error text is empty and
the analysis reports 52 rules with 0 results. Static analysis success does not
convert the failed CI run into successful evidence.

## Local verification

```bash
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-restore --filter 'FullyQualifiedName~DesktopLocalPairingRuntimeTests'
dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~DesktopLocalPairingRuntimeTests'
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~DisposeRejectsAndStopsCancellationIgnoringLateEnableSession' || exit 1; done
for run in {1..20}; do dotnet test tests/Flowspan.Desktop.Tests/Flowspan.Desktop.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName~DisposeRejectsAndStopsCancellationIgnoringLateEnableSession' || exit 1; done
dotnet build Flowspan.slnx --configuration Debug --no-restore -warnaserror
dotnet build Flowspan.slnx --configuration Release --no-restore -warnaserror
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore
git diff --check
```

Results:

- local-pairing runtime class: `29/29` in Debug and Release;
- exact cancellation-ignoring late-enable row: twenty fresh processes in Debug
  and twenty in Release, all passing;
- combined current tree through `858acb2`: solution `2580/2580` in Debug and
  Release;
- solution builds: zero warnings and zero errors; and
- format verification and `git diff --check`: passed.

## Hosted post-fix evidence

Pending successful exact-tree CI and CodeQL after `7239448`. No later hosted run
is claimed here until its exact SHA and terminal results are verified.

## Explicit limitations

This is managed same-host cancellation/cleanup evidence. It does not prove
physical LAN pairing, native firewall behavior, packaged accessibility,
credential-store behavior, signing, notarization, or release acceptance. The
fix closes one late Start classification race; broader load, native networking,
and physical Device evidence remain open.
