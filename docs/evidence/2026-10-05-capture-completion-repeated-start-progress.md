# Same-Capture repeated Start after recorded fatal — 2026-10-05

Status: this finite repeated-Start behavior is accepted locally. Other
task4/task5 and aggregate/native/hosted/v1 gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay, not a
pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Fact: `RepeatedStartAfterRecordedCompletionFatalDoesNotReturnCachedSuccess` in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
The complete RED/Debug GREEN/Release GREEN test bytes are unchanged:
`23220d15d19fe64fac3c0f7f26e97e770cc110c8603c821dd036af6891b13007`.

## Actual RED and minimal repair

The actual Capture action first settles successful Start result=true through
the real one-argument owner/ABI, then its wrapper throws the nested original
fatal. Initial public Start already reports that fatal. The helper finally
joins its real Start/ABI thread, calls Start again before any drain, then
snapshots the actual before-drain facts. This is not a synthetic cached task.

`repeated-start-fatal-red01` is compiled and actually executes1 Failed/0 Passed.
At frozen test line23, repeated Start should report the same original fatal,
but its recorded exception is null: the `startRequested` branch returned the
successful cached TCS. Stop, two Dispose attempts, raw teardown, helper return
and outer GC occur before this assertion. Assertions after line23 are not
claimed to pass in RED.

The only production difference from RED to GREEN is one `ThrowPendingFatal()`
line before returning that cached task. It does not reset the original TCS,
settlement, issued or native-invocation-returned facts, issue another native
Start, or acquire another completion owner. Actual `repeated-start-fatal-green01`
and `repeated-start-fatal-green-release01` each pass1 with the complete same test.
API.

SHA-256: `fca3d0c49f980bd4d2185c78993a1aea5570016298af21a50f37b56e66546084`.

## Bounded behavior and cleanup proof

GREEN confirms before drain: Start issued/returned/settled=true, original TCS
RanToCompletion with result=true, completed/exit notification=true; Stop is
unissued and pending. There is exactly one Start selector/native invocation,
one Start root/copy and no release/removal/barrier/dependent cleanup. Charge is
`before+1`. Repeated Start throws the same recorded fatal without another
SourceUnavailable notification. Its result local remains defaultfalse because
assignment never completes; this is not an API returning BOOL=false.

Actual final Stop/drain and both Dispose attempts still report that original
fatal. Confirmed independent cleanup releases both known callers/roots once,
Stream/output/configuration, queue and retained source; both primitive native
retirement/managed-drain facts are terminal. Final counts are2 roots/copies,
2 releases,2 root frees and0 live roots/blocks. Shell/accounting returns to
`before`; after raw teardown/helper exit and outer forced GC, weak Capture,
completion, operations, source and callback-marker graphs are unreachable.
Before-drain snapshots strongly hold Capture in the helper: they are not a
pending cross-boundary weak-graph/GC retention proof or a general active-user join.

## Regression and independent audit

Actual `focused-debug-repeated-start-fatal01` /
`focused-release-repeated-start-fatal01` each pass143. Qualified identity proof
preserves all142 previously accepted identities and adds only this Fact, with
no removals. Stdout/TRX agree on all single/focused outcomes, with0 skipped or
nonterminal results. The five stages bind503 selected inputs and complete
138-file runtime inventories before/after. Standards:0 hard/0 judgment findings.
Spec:0 concrete findings.

Root actually executes `repeated-start-fatal-root-saved01` /
`repeated-start-fatal-root-current01`: both raw exits0, empty violations,
verified receipts and reports byte-identical to their worker counterparts.
Documentation independently reads the frozen source/diff, stdout/TRX and worker/
root reports, verifies root receipts and SHA256s, and compares report bytes.

- Auditor: `91819dffe623767c88c004e77b32eb4d4b83d80d787d6d95a535742bfa8e267b`.
- Replay: `b568018320e4fd0320fa267f459c51703d07bd7fe9534497fe071e812a48371c`.
- Saved report: `19142b763504d63eab1e69344c1b42d8ef278eedbecdca40a1f98640077566c1`.
- Selected-current report: `cea9c16fc3622c233bd29baff207645c9f7f22c97725918655fdc8ed28fcb6ad`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/repeated-start-fatal-replay.sh repeated-start-fatal-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at this freeze, not the whole tree/tools or a pristine commit.
Further active-observer work makes that equality historical; saved evidence is
unchanged. No acceptance is inferred for other cache branches/fault placements,
active failure/completed resource-use joins, self/async joins, races, Stop
equivalents, outer `IsDrained`, full task4/task5/MCC/MSC, native containment or CI.
Last recorded project D/R remains390; no new aggregate/quality/full/native gate.
This documentation step changes only this file and runs no build/test/replay.
