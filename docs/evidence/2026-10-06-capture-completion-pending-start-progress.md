# Pending successful Start leaves late-callback admission open — 2026-10-06

Status: this finite pending-Start behavior is accepted locally. Actual single/
focused GREEN, independent reviews and root evidence audits are confirmed.
Recorded base: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree
overlay, not a pristine commit. Evidence:
`/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Fact: `PendingSuccessfulStartDisposeLeavesAdmissionOpenForLateCallback` in
`tests/Flowspan.Platform.MacOS.Tests/MacOSRemoteWindowCaptureCompletionOwnershipTests.cs`.
Complete single Debug/Release test SHA256:
`3f50f589940e1c3d5c5727994d5b11fc5350ce9452448b4a0738b748ebf8d17d`.
Actual `pending-healthy-start-debug01` and `pending-healthy-start-release01` each
pass1 directly. No production/support change, new RED or repair is claimed.

## Actual pending phase and admission

Start's controlled native invocation takes an actual extra heap retain of the
real one-argument completion and returns before any callback. A dedicated real
ABI thread signals ready, then waits at a local barrier before `runtime.Invoke`.
Ready observation is not callback entry, result settlement or completed/exit.

External Dispose reports InvalidOperationException:
`macOS capture cleanup remains unconfirmed.` Actual pending facts remain Start
issued=true/returned=true/settled=false, original result WaitingForActivation
with no BOOL result, and exit pending. Stop is unissued/pending. Resource-use
admission remains open/active0/join-incomplete; all four Capture/primitive caller
release-attempt/confirmation flags arefalse. Actual callback invokes/returns0,
release/root-free attempts0, no removal/barrier/dependent object/queue/source
cleanup. The native-held copy remains ready/held with0 release attempts and
charge `before+1`; pending FirstFailure isnull. Dispose does not mutate the
pending result into a synthetic failure or close admission to the real callback.

These are strongly held same-helper snapshots, not a pending cross-boundary
weak-graph/GC proof. Native handoff already returned, so this case does not test
an active native-handoff borrowed-pointer lifetime.

## Real late callback and final graph

Finally opens the local barrier, allowing actual typed `runtime.Invoke` with
success, and performs real `Thread.Join` before subsequent test cleanup. It does
not synthesize callback settlement/exit, and completed notification is not Join.
The original Start task yields true with no failure. Actual external Stop/drain
succeeds and both Dispose attempts return without failure.

Final caller release flags are alltrue; resource admission is closed/active0/
join-complete. Known callers and independent Stream-held copy cleanup release
once, with2 roots,3 copies/retains,3 releases,2 root frees,0 live roots/blocks.
Both primitive native-retirement/managed-drain facts are terminal; independent
removal/barrier and object/queue/source cleanup are confirmed. Shell/accounting
returns to `before`. Raw controlled-runtime teardown, NoInlining helper exit
and outer forced GC precede assertions; weak Capture/completion/operations/
source/callback-marker graphs are unreachable. Final GC is not pending GC.

## Regression and completed root audits

Actual `focused-debug-pending-healthy-start01` /
`focused-release-pending-healthy-start01` each pass150: exact149 accepted qualified
identities plus only this Fact, no removals. Stdout/TRX agree on all single/
focused results,0 Failed/skipped/nonterminal. Four stages bind503 selected inputs
and complete138-file runtime inventories before/after; all stage/root receipts
verify. Root actually executes `pending-healthy-start-root-saved01` /
`pending-healthy-start-root-current01`: raw exits0, passed/empty violations and
reports byte-identical to corresponding worker reports. Documentation reads
frozen source/raw results/reports, verifies receipts/hashes and compares reports
without running audit/replay. Standards:0 hard/0 judgment findings. Spec:0
missing/extra/incorrect finite-diff findings. Both reviews are read-only and do
not independently run build/test; actual execution evidence is recorded above.

- Auditor: `5129e83c4f528c15450570ea4eb1393df553e3f61eca0973647c20aa63844554`.
- Replay: `90cba60ddde1fbaf51a86242df4701e30fd99c83ff475422e5a95ce610dd0011`.
- Saved report: `49d671e11a635b1d271f979545dc32f1ad6a23769d1c6a07d18b04817bec9056`.
- Selected-current report: `ac628bae37a19b6a228ce8c22202f68944a573f2304fc47cafb7ed195dc29654`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/pending-healthy-start-replay.sh pending-healthy-start-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at this freeze, not the whole tree/tools or a pristine commit;
subsequent source edits make it historical. Frozen saved evidence is unchanged.
This is not native-borrow/SCK, other observer/fault/race, pending-GC or complete
task4/task5/MCC/MSC/v1 acceptance. Last project D/R remains390; no new full-project,
full-solution/quality/native/CI gate. This step changes only this evidence file
and runs no build/test/replay or new agents.
