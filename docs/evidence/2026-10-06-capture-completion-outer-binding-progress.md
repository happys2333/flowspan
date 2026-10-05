# Real catalog binding with independently held Stop copy — 2026-10-06

Status: finite `PhysicalDrainWithHeldStopCopyDoesNotReturnCatalogBinding` behavior
accepted locally. This is outer refusal/retention evidence, not recovery or full
task5/MCC/v1 acceptance. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

Execution scope: actual managed Catalog/Boundary/Capture ownership with controlled
native effects on this host, not ScreenCaptureKit execution or Windows/Linux
platform verification. NativeSource names identify the production owner class.

## Setup history, not product RED

All earlier attempts are preserved; their unexecuted later assertions are not
inherited as proof:

- `outer-held-stop-copy-debug01`: xUnit1031 compiler exclusion, no executed Fact
  or TRX. Its receipt is valid, but it is not a behavioral RED.
- `outer-held-stop-copy-debug02`: recorder aborted with tar Write error observed
  in tool exit1; no persisted terminal exit/receipt/build/TRX. The partial source
  recording is not a valid frozen test stage or product RED.
- `outer-held-stop-copy-debug03`: incorrect test expectation at line66. Actual
  Stop callback/ABI returns, then native invocation throws ordinary IOException,
  so Capture's InvocationReturned correctly remainsfalse, nottrue.
- `outer-held-stop-copy-debug04`: disposed-catalog GetSnapshot misuse at line100.
  ObjectDisposedException is correct, not an empty snapshot or product defect.

Final `outer-held-stop-copy-debug05` and `outer-held-stop-copy-release01` each
actually execute1 Passed. No production behavior repair is claimed. Setup adds
only an internal thin actual-NativeSource Capture overload; the test API's
optional factory defaultsnull, and the fixture's independent Stop heap retain
defaults off. Existing paths remain unchanged. Frozen setup/final SHA256s:

- API.

  SHA-256: `d43fd02061b8bdc4c6542459fc9de11d32c85c5656c4175274d3741bb08f6316`.

- Completion ownership test: `b2e758e62cbf619788855ee73bf144e5994950dbb58804a4b41c4312c311af54`.
- Catalog test/API fixture.

  SHA-256: `08171d2ce110b79b84d338e1079df073ddfaef3fea6e1d92176d5400b42bfc6a`.

## Actual outer path and retained binding

The test uses actual Catalog→Boundary→same catalog binding NativeSource→Capture,
not a second Capture source or alternative owner. The thin overload creates the
existing NativeCaptureSource adapter; the opt-in test factory forwards real
sample/unavailable callbacks. Existing native source/retain-token effects track
Window/Filter ownership. The pool has catalog1/batch1 capacity.

Stop takes an actual extra heap retain in a slot independent of the Stream-held
copy slot, invokes the real typed one-argument callback successfully, then throws
ordinary IOException after ABI return. Capture Stop facts are issued=true,
returned=false, settled=true, successful result/exit=true. Actual cached drain
is terminalfalse and reused; physical `IsDrained` istrue. The existing real
Boundary fallback therefore tries native Dispose, not a fabricated fallback.

Native Dispose releases both known callers and dependent Stream/output/
configuration, queue and retained source token once, but the independently held
Stop copy keeps native retirement and managed drain unconfirmed. Runtime snapshot:
2 roots,3 copies,2 releases,1 root free,1 live root/block. Stop FirstFailure isnull;
its owned caller release is confirmed while both terminal tasks remain pending.
The Stream-held slot iszero with0 release attempts; the external Stop slot stays
held with0 release attempts. Capture charge remains `before+1`.

Actual outer Dispose reports `macos_capture_cleanup_unconfirmed`. Binding's
disposed flag remains0 and entry references remain2. Catalog Dispose then retires
its own reference, correctly making GetSnapshot throw, while binding disposed0,
entry references1, base Window/Filter references1 each and pool usage(1,1,0)
persist. Replacement Refresh is refused with
`macos_source_ownership_capacity_exhausted` before enumeration calls0. This is
confirmed retained ownership/capacity, not silent binding return or over-admission.

Finally releases only the confirmed fixture-owned extra copy once and disposes
the raw controlled runtime. It does not retry Capture/Boundary cleanup, return
binding, clear quarantine or reset retained-owner accounting. Final Capture
count remains `before+1`; no post-release recovery is asserted. All pending
observations strongly hold the graph in this test: they are not cross-boundary
weak-graph/GC proof. Healthy-positive and late-fatal-negative controls remain open.

## Regression and independent audit

Actual `focused-debug-outer-held-stop-copy01` /
`focused-release-outer-held-stop-copy01` each pass149: exact148 prior qualified
identities plus this Fact, no removals. Final single/focused stdout and TRX agree,
0 skipped/nonterminal. Six recorded executable stages bind503 selected inputs
and complete138-file runtime inventories before/after; excluded setup is separate.
Standards:0 finite-code findings. Spec:0 finite-scope findings.
Root actually executes `outer-held-stop-copy-root-saved01` /
`outer-held-stop-copy-root-current01`: raw exits0, passed/empty violations,
verified receipts and reports byte-identical to corresponding worker reports.
Documentation reads frozen source/diff/raw results/reports, verifies available
stage/root receipts and hashes, and compares reports without executing replay.

- Auditor: `7adb6d8e5d037782856b233a26b837cd33eda8e156d66a1e1691f17e6bce624e`.
- Replay: `9959bdab5000045e0baf1d8004ac0dab87994bb334fae146d40a7f61f68e0e29`.
- Saved report: `0ba8bc606a34103522fcd4938d45250cd9a6eaf1ebac2136d9c882cb3959fe7e`.
- Selected-current report: `9c1e5cdc4f48ab6854cb8ba5cef9e21395aa7adebecd6ab6388796b7bec2da95`.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/outer-held-stop-copy-replay.sh outer-held-stop-copy-next-saved01 saved
```

Use a fresh label; replay runs no tests. Current equality covers503 selected
test/build inputs at this freeze, not the whole tree/tools or a pristine commit;
next source edits make it historical. Frozen saved evidence remains unchanged.
Full outer debt/recovery, other fault placements/unknown effects/races and full
task4/task5/MCC/MSC remain open. Delegate0, max16 uncomposed, ProtectionUnknown and
production-unavailable limits are unchanged. Last project D/R remains390, not
rerun; no new full/quality/security/native/CI gate. V1 and the long-term Goal
remain open. This step edits only this file and runs no build/test/replay/agents.
