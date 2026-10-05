# Same-Capture Start body/pool failure-order checkpoint — 2026-10-05

Status: one finite MCC task4 combination is accepted locally. Task4, complete
MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`StartInvocationBodyFatalThenPoolPopFatalPreservesOriginalFatalAndCaptureGraph`
uses the actual one-argument completion. Construction is healthy. The real
Start completion callback returns before controlled invocation throws a nested
original OutOfMemoryException A after that effect. Ordinal2 pool pop then
consumes its effect and throws a wrapper containing a distinct fatal B. Stop,
output removal and queue barrier are healthy; token/ordinal/creating-thread
push/pop records match and no unknown effect is retried.

Compiled `start-body-pool-red01` actually fails line30: `Assert.Same` expects
original Start body fatal A but observes later pool fatal B. Known independent
cleanup, retired primitives, retained count and complete weak graph already
pass before that identity assertion. This RED isolates first-failure ordering,
not graph loss, a compiler failure or incomplete cleanup.

Final GREEN02 adds only seven lines to the shared `InvokeCompletion` body:
catch the body exception, call `RecordFatal` before the known pool unwinds,
then rethrow. The existing first-fatal slot now retains A despite subsequent B.
Start, Stop and both Dispose calls expose that same original A. Both caller
references and stream/output/configuration/queue/source clean up once; both
primitives retire with no FirstFailure and zero live blocks/roots. Unconfirmed
Start pool debt still retains the shell root/accounting and full weak graph
after raw fixture teardown and outer GC. Native effects remain outside gates.

RED01 and final GREEN02 have identical complete test bytes. Single Debug/Release
each1 and focused Debug/Release each125 pass, preserving the exact124 accepted
qualified identities plus this1 Fact with no removals. All five final locked
restore/builds exit0 with0 warnings/errors; GREEN test exits0 with no skipped
or nonterminal results.503 selected inputs, complete138-file runtime inventories,
raw commands and qualified TRX/DLL bindings are saved and receipts verify.
No new project/quality/full-solution/native or hosted gate was run; the last
actually executed project Debug/Release remain390.

`start-body-pool-green01` is a preserved actual passing candidate with the same
test, but conditional Start-only recording was superseded by the final shared
rule. Its separate source/runtime receipt is verified and explicitly classified;
it is not substituted for the final Debug/Release/focused GREEN02 evidence.

- Test SHA256: `ff335dfe577203a0502e0e9f17d7990e8f3f6480991d6df4ab0e1319ca40428c`.
- Final API.

  SHA-256: `d42ea8a5f17a94f9bc7440d546623816c6ce2b3f824d4f04b70d6eb28323d1c2`.

- Auditor: `1aed4e25e78face40823e2aa91766dd3ad3e16a420534c8393f71a5ed7d72bd4`.
- Replay: `de9d6cbf32ac5bba4e4b5a88259eea4b57dff4a8a53df67f384422a375cfab1c`.
- Saved report: `1eeec0be5d0edfab29f12e9322051afba2458549c535bd901a54c1297ce82a9e`.
- Selected-current report: `d32e3f2c5022b48a2e735b8fb30bbe84ccc27909150f70ea681c1570c0b89a56`.

Standards:0 concrete findings. Spec:0 concrete findings. Root accepts this
finite behavior after actually executing `start-body-pool-root-saved01` and
`start-body-pool-root-current01`: both raw exits0, empty violations and reports
byte-identical to their worker counterparts. Both replay receipts verify.
Replay executes no tests. Selected-current equality covers503 inputs at the
recorded freeze, not the whole tree, and becomes historical after later edits.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/start-body-pool-replay.sh start-body-pool-next-saved01 saved
```

Use a fresh label. The shared method also changes Stop ordering, but Stop's
body/pop combination is not executed or accepted by this checkpoint. Cached
Start outcomes, zero/unknown pool acquisition, observer/resource-use joins,
races/reentry and outer-drain fallback remain open. This is portable controlled-
effect evidence, not native-fault containment, global Capture admission,
production sharing or complete task4/MCC/v1 acceptance.
