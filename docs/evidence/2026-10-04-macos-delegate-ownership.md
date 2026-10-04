# macOS delegate ownership prerequisite — local evidence

Status: final local implementation and synthetic ABI evidence; exact-commit
Windows/macOS/Linux CI remains pending. This does not close native Task 6 or v1.

Scope is the [MDO specification](../../specs/v1/native-remote-window/macos-delegate-ownership/requirements.md)
and [ADR 0029](../adr/0029-direct-csharp-screencapturekit-interop.md).
The owner is not wired into capture; current ScreenCaptureKit construction
still passes `delegate=0`. Production Remote Window host sharing remains
unavailable. No new NuGet dependencies or production-language toolchain were
introduced; this is an independent clean-room C# implementation.

## Executed environment and inputs

- Local macOS 27.0.1, build 26A434, ordinary arm64; .NET SDK 10.0.301 and
  executed runtime 10.0.9. Neither macOS 15.2 nor Intel/arm64e was executed.
- Baseline HEAD `eea18ad22e66e3b10aaf87af612e945ead35547e` plus the scoped
  uncommitted owner/tests/tool/IVT/workflow changes. Local results are source-
  hash bound, not an inherited hosted result from this baseline commit.
- Owner SHA-256:
  `6aa506c30880383e1c5a99e7898c7ac93e22e6e1a93ef6d32767c350b1cc75e8`.
- Tests SHA-256:
  `dd7f20854138f60a41d542707000ced55a643d724ce0dcc4a05365d0f1189849`.
- `NativeDelegateProbe.cs` SHA-256:
  `9b50d293c4dc02e79395171ba274a5c3341efcbb51983de32612fd31905262ba`.
- Complete tool/project/lock/IVT and isolated DLL hashes are preserved in
  `/tmp/flowspan-macos-delegate-probe-20261004/final-input-and-output-hashes.log`.

## Portable behavior and actual RED→GREEN

Twelve behavioral stages have executed failing TRX followed by passing TRX:
terminal without sample; early terminal; binding mismatch; blocked retirement;
immutable address/late isolation; direct self-join; Task.Run descendant
self-join; handler/OOM faults; wrong-stream before activation; invalid signal;
depth-65 original OOM; and published/unbound zero-stream initialization.
The handler/OOM stage has two failing cases; each other stage has one.
The root independently parsed these RED/GREEN records. Capacity and exited-
ExecutionContext cases are supplemental GREEN, not manufactured behavior RED.
Compilation failures and test-writing mistakes are preserved separately.

Standards review found that an old handler's `Assert.Fail` could be contained
by the owner, making a negative test ineffective. The final test observes zero
old notifications and null Failure instead. Spec review found that a published
owner's failed zero bind allowed later initialization. The executed stage-14
RED is repaired by permanently retiring that published owner; a pre-publication
zero attempt remains nonterminal. Final two-axis owner review and probe/CI review
must refer to these final hashes, not the older index contents.

Final focused Debug/Release each pass 18/18. Full MacOS project each passes
144/144. Owner lifecycle records and commands are in
`/tmp/flowspan-macos-delegate-20261004/OWNER-HANDOFF.md`.

## Complete local solution gate

The root actually executed locked restore, complete no-change format, Debug and
Release warning-as-error builds, and both complete solution test runs:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Debug --no-restore
dotnet test Flowspan.slnx --configuration Debug --no-build --no-restore --logger 'trx;LogFilePrefix=mdo-debug' --results-directory /tmp/flowspan-mdo-solution-debug-results-20261004 --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFilePrefix=mdo-release' --results-directory /tmp/flowspan-mdo-solution-release-results-20261004 --blame-hang --blame-hang-timeout 3m --blame-hang-dump-type none
```

Both builds report zero warnings/errors. Each configuration has exactly 12 TRX,
2710 total/executed/passed, all non-success counters zero, every result Passed,
and matching per-assembly counts. Independent parser and result:
`/tmp/flowspan-mdo-qa-audit-20261004.py` / `.json`; JSON SHA-256
`0f05ef598228cd4e520691d6cf70511686b7ede43135e398ac09f0fda23954f6`.

Explicit TEST MODE composition and deterministic simulator return exit 0.
The 26-project NuGet including-transitive vulnerability query returns exit 0,
empty stderr, no query errors or reported vulnerable packages. This is a
point-in-time advisory query, not proof of general safety. Local checks are
macOS execution of portable contracts, not Windows/Linux native evidence.

## Actual no-capture Objective-C/GCD execution

The independent tool's locked Debug/Release builds and all four modes were
actually executed on the final owner: default/help return explicit Skip with
zero probe-native calls, unknown arguments return expected exit 2, and explicit
`--run-synthetic` returns exit 0 in both configurations. Full standalone format
also passes. See [reproduction commands and limits](../../tools/Flowspan.MacOS.DelegateProbe/README.md)
and `/tmp/flowspan-macos-delegate-probe-20261004/RESULTS.md`.

Both final configurations actually observed 61 native callbacks and four
published bridges. The root independently restored/built the standard Release
output, ran full tool format, and executed synthetic mode again. Its stdout is
277 bytes, exactly one LF record, SHA-256
`ca64ed8dd8591c786457c2f84ece3885b4245a0ef155d10f785fb06a7e19fdf5`;
stderr is zero bytes. Actual record:

```text
delegate_probe=pass; mode=synthetic; method_signatures=3; native_callbacks=61; managed_invocations_exited=true; published_bridges=4; published_bridges_retained=true; capture_executed=false; SCStream_creations=0; AppKit_initialized=false; permissions_requested=0; pixel_reads=0
```

The three ordinary-arm64 signatures derive from SDK SCStreamDelegate declarations,
but runtime metadata belongs to this synthetic NSObject subclass. Typed
Objective-C messages/GCD callbacks actually exercise early terminal, forced GC,
blocked-handler concurrency, direct/descendant self-join, retirement and late
tombstones. No ScreenCaptureKit session, SCStream, AppKit, window, permission
preflight/request, source enumeration, title, sample, pixel or input API runs.

`dotnet build --locked-mode` was rejected by MSBuild before compilation; logs
remain preserved. Reproduction docs now use the actually successful
`-p:RestoreLockedMode=true`. This command error is not counted as behavioral RED.

## Strict CI evidence gate and remaining work

CI builds/formats the tool on all three OSes and requires the no-native default.
Only matching macOS runs explicit synthetic mode, with a two-minute step limit
and raw stdout/stderr uploaded even when the step fails. Native Pass requires
exit 0, empty stderr, a bounded exact one-line schema and no NUL; Skip cannot pass.
CodeQL explicitly builds the standalone source for extraction.

An independently reproduced gate defect accepted embedded NUL because Bash
command substitution discarded it. Actual fixture RED incorrectly exited 0;
the raw-byte NUL check repairs this, and all 26 fixtures plus actionlint pass.
Files: `/tmp/flowspan-macos-delegate-ci-gate-tests.py`,
`/tmp/flowspan-mdo-ci-gate-nul-{red,green}-20261004.log`.

Published +1 NSObject bridges and immutable mappings are deliberately retained
until process exit, with a fixed four-slot probe budget. This is not cleanup,
production memory boundedness or native drain. `ManagedInvocationsExited` proves
only admitted managed handlers have exited; no-future-native-callback,
SCStream delegate retention/unbinding, real source loss, capture drain, TCC,
protection, input, independent Emergency Stop, physical devices, signing,
notarization, legal clearance and full v1 remain open. Exact new-SHA hosted CI
and downloaded raw evidence must be recorded separately before closing task 4.
