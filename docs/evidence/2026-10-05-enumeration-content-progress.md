# macOS enumeration content — portable progress checkpoint

Status: partial implementation of MEP1-MEP8 on `codex/v1-foundation`.
Only enumeration task 1 closes. Tasks 2/3 are in progress; native primitive
task 4 and final native/exact-SHA hosted gates remain open. Goal stays active.
This is not complete enumeration, production sharing, release or v1 acceptance.

## Implementation and actual behavior

Production and controlled system effects use the same EnumerateCoreAsync and
CreateSourceCore. The original bounded BatchRecord holds the enumeration ledger
before content ownership effects. Unknown retain/release results remain charged
without guessed release/retry. Exact context/ledger checks protect settlement;
reentrant settlement cannot detach an in-flight content graph.

One atomic result admission prevents duplicate retain. Ordinary query failure
closes the producing context. Independent content, second autorelease-pool and
confirmed source cleanup failures are collected without skipping known sources
or replacing an earlier nested fatal. The source-cleanup tracer creates an actual
NativeSource through CreateSourceCore, not a substituted source implementation.

The invocation counter joins the current callback set through its first idle
boundary. The overlap tracer holds the admitted invocation before its exit
observer and exits a duplicate first; content cannot be released at that point.
This does not prove final native ABI return, exclusion of later entry or physical
native-copy retirement. `IsContentLifecycleEnded` is not a complete lifetime proof.

## Preserved RED/GREEN and regression records

Raw records: `/tmp/flowspan-enumeration-producer-20261005/`. Each behavior stage
has exact command/cwd/SDK/exit/TRX, frozen Git baseline plus explicit overlays,
source before/after and runtime manifests. Specification copies are separate
traceability data, not compiler inputs.

Actual RED→GREEN pairs are 02/03,04/06,07/08,09/10,11/12,13/14,15/16,17/18,
19/20 and 21/22. Full test-file bytes and complete qualified identities are
identical within each pair. Their GREEN inventories grow from 1 to 12 cases.
Stage 02 fails before forced GC; only its GREEN counterpart reaches those GC
assertions. Stage 01 CA1001 and stage 05 CA2219 are compile candidates, not
behavioral RED or GREEN. All candidates and failed assertions remain preserved.

| Stage | Actual result |
| --- | --- |
| 22 focused Debug | 12 Passed, no non-success counters |
| 23 project Debug / 24 project Release | 329 Passed each, exact 317 baseline + 12 |
| 25 solution Debug / 26 solution Release | 12 TRX and 2900 Passed each, exact 2888 baseline + 12 |
| 28 quality | Locked restore, format verify and live-worktree diff check each exit 0 |
| 29 frozen Release repeats | 10 independent runs × 12 Passed = 120 executions of 12 unique cases |

Every regression result/definition/entry execution binding, stable identity map,
runtime test assembly and full inventory is checked. All stages23–26 have the
same 754 frozen source files as stage 22. Repeat source and runtime bytes remain
unchanged. These are actual local Darwin arm64 SDK 10.0.301 portable tests, not
Windows/Linux native execution or new hosted evidence.

Quality stage 27 has three successful command receipts but its recorder's final
comparison fails: `rg` outside a Git checkout did not apply `.gitignore`, so 329
generated bin/obj files entered the after inventory. The original 754 files
remain byte-identical. Stage 28 explicitly applies the archived ignore policy;
its 754 selected source files remain unchanged and a separate 1083-file inclusive
inventory binds all 329 generated files. Stage 27 is a recorder candidate, not a
product behavior failure. No main-harness exit receipt was saved there; root
observed exit 1 in the tool result. The diff-check receipt targets the live
worktree, not a byte-bound complete diff snapshot.

## Independent root replay

Root actually executes both immutable offline validators, exit 0,
`violations=[]`, with output byte-identical to their frozen reports:

```sh
python3 /tmp/flowspan-enumeration-producer-20261005/audit-content-progress-through22-script.py
python3 /tmp/flowspan-enumeration-producer-20261005/audit-enumeration-progress-gates-final-script.py
```

- RED/GREEN validator SHA-256:
  `6fe5f95194affc11c867da4d1cf5326c73fe0356da96a98fd632488b9bc7a3dc`.
- Its frozen report/root output SHA-256:
  `263d03d2c490e09daad36a82dbb460c2880f2634360f4f8cb0b1b68c8141eeba`.
- Progress gate validator SHA-256:
  `4ca3176414474d3501177910193f6cd5d245ccffcb78e5e8c2ae97db6d3bd911`.
- Its frozen report/root output SHA-256:
  `bbc62c5078213b2bb5156fedcb7073854d2171c05b8054e5af2f9c548c5b2a6d`.

The through22 validator escape-error candidate and progress gate sample1
recorder-mismatch candidate are preserved separately. Neither is product RED.
Hash inventories do not prove compiler inclusion or per-file CodeQL extraction.

## Next implementation entry and unchanged boundary

Continue healthy nonempty source transfer, exhausted capacity, stale/reused exact
contexts, first-pool/dispatch faults and full late/concurrent callback admission.
Then implement the actual inert primitive owner → same-batch attachment →
AcquireCopy contract. Release the caller-owned Block reference before waiting
for real physical capture/root retirement; independently join managed invocations.
An actual extra native heap-block copy must prevent batch return until its last
release. Copy-helper counts, the first idle latch and owned `IsReleased` are not
retirement evidence. Preserve Capture callers' existing contracts; the new staged
path must not use a finalizer/retry to settle unknown release effects.

No new native probe or exact-SHA hosted CI/CodeQL was run for this progress.
The previous `5f62eb8` checkpoint is not inherited. Production remains
`delegate=0`, macOS14.2/Arm64 candidate, Protection Unknown and sharing unavailable.
Global Capture admission, secure input/protection, Emergency Stop, physical LAN,
platform minimums, native accessibility, independent security review and signed
package lifecycle/v1 acceptance remain open. See the
[enumeration tasks](../../specs/v1/native-remote-window/macos-enumeration-producer/tasks.md).
