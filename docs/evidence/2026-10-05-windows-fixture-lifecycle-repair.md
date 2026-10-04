# Windows hosted test fixture lifecycle repair

Status: local portable repair verified; a new exact-SHA Windows CI result is
still required. No production pairing or permission code changed.

## Preserved failed checkpoint

Exact `f9896b82f60a375264743a9ebb5f43b32b5e785e`, CI `37220623934`, attempt 1,
Windows job `111490043635` failed a portable MacOS permission test and aborted
Desktop with one unfinished pairing case. Its downloaded records are audited
in [the router evidence](2026-10-05-macos-stream-delegate-router.md). The
testhost's output-file error is an observation, not proof of disk exhaustion.
The original runner scheduling trajectory was not observed or reproduced.

## Pairing fixture

The deferred test scheduler captured a publication action but did not run it.
On an earlier exception, the fixture skipped `publish()` and entered source
Dispose, which correctly waited for that queued publication. A bounded
injected-failure loop demonstrated this cleanup deadlock; external publication
release immediately restored the original failure.

The shared fixture now releases the cancellation gate, joins the cancellation
task, and drains publication in an inner finally. A short lock transfers only
the publication action; invocation occurs outside it. Work scheduled after
cleanup begins is also released. Three regression rows exercise failure before
worker capture, before publication, and an actual cancellation callback failure
propagating from its join. Test rescue and normal drain share an atomic once
gate; outside assertions require one real run and one normal invocation.
The original highest-sequence assertion and five-second bounds remain.

Actual final pre-fix execution fails all three new rows with TimeoutException;
each is rescued and drained. Final Debug/Release pairing classes pass 10/10
each; four fresh single-processor-count processes pass 16/16 combined.

Frozen test SHA-256:
`fa310fee77b92417439c14d4e0e0a694fff22af36e09e8478a0531868db9947c`.
Unchanged production SHA-256:
`68d87b2f4e8663ccf757395760f4d8b7eb614c644423d931e03babcb14b5aa9c`.
Raw logs, RED source/TRX, commands and detailed diagnosis are in
`/tmp/flowspan-windows-pairing-f9896b8/diagnosis.md`.

## Permission race fixture

The callback's autonomous five-second watchdog could release the permission
commit gate before a scheduled contender or test continuation arrived. The
old test did not check that precondition before its negative-time assertion.
Three event-driven diagnostic runs explicitly expire that watchdog, observe
the actual mutation failure, and then release a dedicated contender: all
reproduce the exact original `Assert.NotSame` symptom. This proves a fixture
defect, not the unique original Windows timing or a production-state defect.

The repaired fixture uses two dedicated threads. Only its outer finally
releases invalidation, and both joins are attempted before either result is
asserted. After the contender's entry signal there is no fixture wait; the test
positively observes lock contention or premature completion, checks invalidation
has not exited, then releases and observes both results. Snapshot, permanent
old-registration invalidation, PermissionDenied and absent new registration
assertions remain. A temporary early-release mutant is correctly rejected and
was removed. No timeout was enlarged and no production hook was added.

Final target Debug/Release each pass 1/1; permission classes each pass 39/39;
four fresh single-processor-count targets pass 4/4. Eight final TRX sum to
84/84 with all non-success counters zero. Test count is unchanged.

Frozen test SHA-256:
`4bb674be60fabc114514c57b7173aa3d661c093317b6704a69d5f8b8e2a06c5b`.
Unchanged production SHA-256:
`9f5a493a27001c4beb3a5d912f447a9522e2048801435cd56990d79f9a48b123`.
Detailed diagnosis and repeatable audit are in
`/tmp/flowspan-permission-race-f9896b8/`; `audit.json` SHA-256:
`762b064cbf694f2a71b3886ef50e2fce5634a01c3ba797377891bb5a68db4fda`.

## Combined local verification

Actual macOS 27.0.1/build 26A434, ordinary arm64, .NET SDK 10.0.301/runtime
10.0.9. Locked restore, no-change format, Debug/Release warning-as-error builds
and both complete solutions pass. Each solution has 12 TRX / 2746 Passed;
Desktop is 760 and MacOS is 177. All summaries are Completed, non-success
counters zero and complete inventories equal. Three new pairing theory rows
explain the increase from 2743; no permission test was added.

The inventory SHA-256 is
`0eef9bf4431a094af0c0629af09dbb7e09ed97789746dd12361e37d2d21aa9cb`.
TEST MODE composition and protocol-1.7 simulator pass. The 26-project
including-transitive vulnerability query reports no packages or errors and
zero-byte stderr. This is a point-in-time query, not universal security proof.
Independent Standards and Spec static reviews have no remaining findings.

Full logs/TRX: `/tmp/flowspan-checkpoint-local-20261005/`. Recheck with:

```sh
python3 /tmp/flowspan-checkpoint-audit-20261005.py
python3 /tmp/flowspan-permission-race-f9896b8/audit.py
```

Combined `audit.json` SHA-256:
`7db75e8ea017e99862286204d779e4f837dfb2c75cc3e2bbce7ceaad9adb63ff`.
The first audit also verifies the independent no-capture Foundation records,
not Windows-native APIs. Local success does not replace a new exact-commit
Windows/macOS/Linux CI result, physical-device evidence or v1 release gates.
