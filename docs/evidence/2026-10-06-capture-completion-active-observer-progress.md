# Same-Capture active completed/failure observers — 2026-10-06

Status: this finite active-observer pair is accepted locally. Both slices are
direct GREEN; other task4/task5 and aggregate/native/hosted/v1 gates remain open.
Recorded base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree
overlay, not a pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Facts: `StartCompletedStillActiveDoesNotReleaseCaller` and
`FirstStartFailureObserverStillActiveDoesNotReleaseCallerAfterDuplicateCompletion`,
in `tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Completed is introduced/verified first, then failure is added. Actual stages
`active-completed-direct-debug01` / `active-completed-direct-release01` and
`active-failure-observer-direct-debug01` /
`active-failure-observer-direct-release01` each pass1. Stdout/qualified TRX agree
on1 executed/Passed,0 Failed/skipped/nonterminal; all four receipts verify.
No production modification, new RED or repair is claimed for this pair.

## Real guarded observers and pending release gate

Start performs an actual extra heap retain of its real one-argument completion
and returns before callback. Real threads subsequently invoke that retained
pointer through the typed ABI. Production owner guards cover the actual action,
failure and completed delegates; opt-in test wrappers pause after invoking the
real Capture observer, not inside a detached mock action.

Completed pauses after actual Capture completed publishes exit=true. Start is
issued/returned/settled, result=true, yet actual ABI return is still pending.
Failure's first successful Capture action settles result=true and its one-shot
wrapper throws original fatal A. The real Capture failure observer handles A
then pauses. A second actual successful callback runs and its thread joins;
that duplicate's completed/exit publication does not release the active first
failure observer. The original public Start had returned a pending task before
callbacks; it later yields its first true result with no retroactive exception.

Actual pending Dispose sees closed admission/active1/join-incomplete. Capture
and primitive release-attempt/confirmation flags are allfalse, actual releases0,
and no removal/barrier/dependent native/source cleanup occurs. Extra stream-held
copy remains held with0 release attempts and charge `before+1`. Completed
Dispose reports InvalidOperationException; failure Dispose preserves original A.
Pending snapshots hold Capture strongly in the helper; they do not prove pending
cross-boundary weak-graph/GC retention.

## Real return, known cleanup and terminal graph

Every helper finally opens its barrier and joins its actual ABI thread(s)
before Stop/drain, both final Dispose attempts and raw test-runtime teardown.
Completed notification alone is not this join. Final read-only facts confirm
closed admission/active0/completed join and all four caller release flags=true.
Both known callers release once; independent Stream release retires the actual
extra retained copy. Totals are2 roots,3 copies/retains,3 releases,2 root frees,
0 live roots/blocks, with both native-retirement/managed-drain facts terminal.
Removal/barrier, Stream/output/configuration, queue and retained-source cleanup
are independently confirmed. Charge returns to `before`; after teardown,
NoInlining helper exit and outer forced GC, weak Capture/completion/operations/
source/callback-marker graphs are unreachable. Completed Start/drain succeed
without failure. Failure Start's already published true result remains; drain
and both final Dispose attempts still report the same original A.

Frozen SHA256 bindings:

- Completed-only test: `34b120ba0bc424fbc9a2ce59077a883dfe2b0d9419ab682435991ec1fd76df02`.
- Final pair test: `fba783743b040efee0aec5c93cae344c61c7ffe122d4f6618ce823d23815c15a`.
- Unchanged API.

  SHA-256: `fca3d0c49f980bd4d2185c78993a1aea5570016298af21a50f37b56e66546084`.

- Unchanged owner: `2fb11b11a464c9dffe55383456132b35b7605429215a9a5b6732d6a4dc148e35`.

## Finite matrix acceptance and audit bindings

Actual `focused-debug-active-observer-pair01` /
`focused-release-active-observer-pair01` each pass145,0 Failed/skipped/nonterminal.
The exact143 prior qualified identities plus these2 remain, with no removals.
All six stages bind503 selected inputs and complete138-file runtime inventories
before/after. Standards:0 hard/0 judgment findings. Spec:0 concrete findings.
Root actually executes `active-observer-pair-root-saved01` /
`active-observer-pair-root-current01`: both raw exits0, empty violations, verified
receipts and reports byte-identical to their worker counterparts. Documentation
independently reads reports/stdout/TRX, verifies root receipts/report/script
SHA256s and compares worker/root report bytes without executing audit/replay.

- Auditor: `c8e8d26a9d322e419871b25992df7c058be22bec1931ada46adf0fc7aedbb4ed`.
- Replay: `40edf80f28c6569df6e47bb0e609d671af1509a13e6fbd056ce273b883cec27b`.
- Saved report: `3ae8d0c4fbde4e4bf0a25b39a01e64ea7967b1359253200be9924529af9ddbaa`.
- Selected-current report: `1817d77e1c42219841e7a0045202990cec1bcc2bb690ed95492fdde7c0af59bd`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/active-observer-pair-replay.sh active-observer-pair-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at this freeze, not the whole tree/tools or a pristine commit;
later source edits make it historical. Frozen saved evidence is unchanged.
No complete acceptance is inferred for all observer placements/duplicates, self/async joins, races, Stop
equivalents, outer `IsDrained`, complete task4/task5/MCC/MSC, native containment,
hosted CI or v1. Last recorded project D/R remains390; no new aggregate/quality/
full/native gate. This documentation step edits only this file, reads frozen
evidence and runs no build/test/replay.
