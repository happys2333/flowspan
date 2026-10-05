# Same-Capture confirmed-acquired/unissued Stop caller checkpoint — 2026-10-05

Status: this finite independent caller cleanup is accepted locally. Task4,
complete MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`StopConfirmedAcquiredButUnissuedCallerIsReleasedOnceWithoutInventingStopProof`
has healthy constructor/Start and normally returned Stop staged acquisition,
then Stop pool push returns zero before handoff. Actual Stop, repeated Stop,
both Dispose calls and cleanup snapshots precede raw teardown/outer GC and
final assertions. No fatal is injected. Stop/repeated Stop return false without
outward exceptions; both Dispose calls report ordinary InvalidOperationException.

Compiled `stop-unissued-caller-red01` actually fails outer line23. Expected
caller releases/root frees/live roots/live blocks are `(2,2,0,0)`; actual is
`(1,1,1,1)`. Only Start caller is released; the confirmed acquired but never-
issued Stop caller remains despite its completed unique setup flow. Snapshots
are taken before raw teardown, so fixture cleanup cannot satisfy this assertion.

Minimal GREEN changes only the API. Local `acquisitionReturnedConfirmed` becomes
true only after staged `AcquireCopy` returns normally. The unique ConfirmStop
flow's final unwind then publishes monotonic `stopSetupSettledUnissued` only if
no handoff was issued. Dispose consumes that eligibility through the existing
gate-free single-attempt caller release path. A transient `!stopIssued` while
acquisition/setup can still resume is not authority. Unknown root/copy outcomes
do not gain this eligibility; issued Stop handling is not broadened.

Both known callers now release/free once; both primitives retire normally with
no FirstFailure and zero live blocks/roots before raw teardown. Stop selector/
native invocation/callback/returned/result/exit and `stopSettled` remain unset.
The new setup-eligibility fact is not callback or completed-notification proof.
Removal/barrier/object/queue/source release effects remain0, physical drain is
false, and no shell-root/count return follows. Full original two-completion/
primitive/shell/operations/source/marker graph and `before+1` charge remain
after teardown and GC despite the independent caller cleanup.

Before RED, the older Stop-zero refusal Fact was narrowed to its accepted
refusal/graph scope with derived consistent caller counts; its reused helper
adds optional actual repeated Stop and a stopSettled snapshot. Historical frozen
evidence is not rewritten. The matrix Stop test/helper bytes remain unchanged.
RED01→GREEN01 uses identical complete test bytes; only API changes for GREEN.

Single Debug/Release each1 and focused Debug/Release each136 pass, preserving
the exact135 accepted qualified identities plus this1 Fact with no removals,
including prior unknown root/copy and issued Stop identities. All five locked
restore/builds exit0 with0 warnings/errors; GREEN test exits0 with no skipped
or nonterminal results.503 selected inputs, complete138-file runtime inventories,
raw commands and qualified TRX/DLL bindings are saved; receipts verify. No new
project/quality/full-solution/native or hosted gate was run; last project D/R390.

- Test SHA256: `41524af837f23f74d45fa61e1e27f982f8abca10e5bfaa36e66d7cecfc39f2ff`.
- GREEN API.

  SHA-256: `7de3e8fe82b010bd6c74f561004dc6131ef20047959c24e43b8dfff5fea01eda`.

- Auditor: `4d10c6829a32b79660520dd6f364a046c70f006536b6d40a85d9ea064f212f10`.
- Replay: `8ea6ad84ca6d2ab69e853afc367f83293e97c101f5a9c0ab1e2be6a11ca27412`.
- Saved report: `d78ced572f72a2a183f1822147b34fb0b89594de56cb2e7308a3a28af909e1ad`.
- Selected-current report: `17f24ee72b1d1730a17539607ca8403e767c6ed63b243ff230dfe8c2a02d4b69`.

Standards:0 concrete findings. Spec:0 concrete findings. Root actually executes
`stop-unissued-caller-root-saved01` / `stop-unissued-caller-root-current01`:
both raw exits0, empty violations, verified receipts and reports byte-identical
to their worker counterparts. Replay executes no tests. Selected-current
equality covers503 inputs at that freeze, not the whole tree, and becomes
historical after edits; saved evidence remains immutable.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/stop-unissued-caller-replay.sh stop-unissued-caller-next-saved01 saved
```

Use a fresh label. This does not accept acquisition races, observer/resource-use
joins, final ABI return or broader safe lifetime. Those scenarios, outer-drain
fallback and aggregate quality/full-solution/native/exact-SHA hosted gates remain
open. Portable controlled-effect evidence is not native-fault containment,
global Capture admission, production sharing or complete task4/MCC/v1 acceptance.
