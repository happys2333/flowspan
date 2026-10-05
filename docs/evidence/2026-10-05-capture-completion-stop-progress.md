# Same-Capture Stop-copy checkpoint — 2026-10-05

Status: one finite MCC task3 behavior accepted locally. The rest of task3 and
MCC/native/hosted/production/v1 remain open; Goal active.
Base HEAD: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with the preceding
[Start checkpoint](2026-10-05-capture-completion-start-progress.md) overlay.
Immutable evidence: `/tmp/flowspan-capture-completion-stop-20261005/`.

## Actual behavior and minimal repair

Actual healthy typed one-argument Start precedes a second completion copy which
executes its physical copy helper and then throws the original nested fatal.
In `tracer01`, the original shell/actual Stop completion/primitive/operations/
source weak graph, retained charge and fatal identity already pass: direct GREEN,
not a claimed ownership-attachment RED. Actual Stop/two Dispose calls perform
no guessed Stop release or native invocation and no dependent native cleanup.

The same compiled tracer actually fails the independently known Start caller+1
cleanup tuple: expected `(1 release,1 root free,1 live root,1 live Block)`, actual
`(0,0,2,2)`. Undrained Dispose previously skipped even that independent cleanup.
`green01` passes the identical complete test-file SHA256
`a2cd275e498db9931f9ae5941fd311e2166d8e0587eeb3f9a3359bd02b050560`;
only the production Capture API changes within this behavioral pair.

The minimal change separately records normal Start `InvokeCompletion` return.
Undrained Dispose selects that confirmed staged caller ownership under its gate,
releases it once outside the gate, then still throws the original Stop fatal.
The observed completion notification is not resource-use join, terminal managed
drain, native-copy retirement or final ABI return. It confirms no Stop/drain,
returns no shell/root/accounting, and permits no stream/output/queue/configuration/
source cleanup. Actual `IsDrained=false` and all those dependent cleanup counts0
remain asserted. No second Capture state machine or new capacity pool is added.

NoInlining helper returns only weak graph observations and counts. Fixture raw
teardown removes its own native memory/GCHandle roots before forced GC, without
production helper calls/retries or reset of Capture accounting. The original
Capture durable root supplies the surviving graph.

## Final frozen gates and review

After behavioral GREEN, API/test comments are corrected to call the observed
fact a notification, not final callback exit. Old-comment green01 and focused/
project01 passes are preserved superseded candidates. Only focused/project02 and
quality01 bind the final comment bytes; the auditor verifies that this later
delta changes comments only and all previous ownership Fact/helper bodies remain
unchanged.

Final focused Debug/Release each pass **111**, with equal complete qualified sets.
Final macOS project Debug/Release each pass **380**: original377, prior Start2,
and exactly one new Stop Fact;0 removed and all non-success counters0. Each
configuration binds564 selected source files and138 complete runtime files
before/after/actual and every TRX storage/codeBase/DLL byte binding. Locked serial
solution restore and format verification pass; project builds have0 warnings/
errors. These are not new complete-solution or native/hosted gates.

Standards review:0 concrete findings, including gate-free single-attempt cleanup,
fixture weak roots and unchanged legacy non-staged behavior.
Spec review:0 concrete findings for this finite Stop-copy/independent Start caller
scope; notification/drain distinctions and the unproved subsequent tasks remain
explicit. Both axes pin final API SHA256
`7a294994195aa908207b90eacac018f7a1422e4f2ad4e533a9fd98f54a024656`
and ownership-test SHA256
`3f131786d9af138f26885355e7252a38531d87818695af208dffa8862389a0f6`.
Static reviews are not additional executions.

Selected-input manifest SHA256:
`6cb88f44cf2b028102810ed29bcaf2b56e2bec4990dd4d021cfd48050764a932`.
Single-layer auditor SHA256:
`4bc6144fdc9f5dbfe2d47c3486b86f166b4608a82590f95a1fb221e406b74856`.
Replay wrapper SHA256:
`5e4b86f115d2468ea607470eeb9505cb0a249153d14420614f85386d04a7b53e`.
Saved report SHA256:
`a753b5ba3aa4c958fc913778b7160d329876d3f24bc897804cdb2ba47d680caa`.
Current selected-input report SHA256:
`6faaa51ad8710ec27db99653dea754ccbcbe2bfc67afc085318ca99386b62f05`.

Worker and root actual saved/current replays each exit0 with `violations=[]`;
actual `cmp` verifies corresponding reports byte-identical. Current equality is
selected build inputs only, excluding concurrent documentation. Subsequent code
changes supersede that live equality, not the saved proof. Fresh-label replay:

```sh
bash /tmp/flowspan-capture-completion-stop-20261005/replay.sh next-saved01 saved
```

This closes only the named Stop behavior inside task3. Root allocation/invalid
returns, caller-release/late root-free uncertainty, fixed pools, race/reentry,
resource-use joins and complete native-copy/managed lifetime still need their
own tracers. No new one-argument native ABI/SCK or exact-SHA CI runs are claimed.
Production remains delegate=0, Protection Unknown and sharing unavailable;
global admission, nonzero delegate, physical/release, v1 and Goal stay open.
