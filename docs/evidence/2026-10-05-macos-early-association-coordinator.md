# Portable macOS early-association coordinator

Status: portable implementation and final local verification passed. This does
not close MSC task 2b's native gate, actual Capture, parent Task 6 or v1. Exact
new-commit hosted verification is pending; no earlier SHA's results are inherited.

## Source and behavior

The preserved execution base is `9a02589408b945b0af536eecc0c52e996f7393a2`
plus these three frozen files:

| File | SHA-256 |
| --- | --- |
| `MacOSRemoteWindowStreamDelegateAssociationCoordinator.cs` | `5ad673ccc15ac0ea74a15cfb98f651c5de18ec0e737b0a9f727e32341230e2f3` |
| `MacOSRemoteWindowStreamDelegateRouter.cs` | `ed4975c2440124218ba9e6c3117669912e8061842c346e169a2cc1a6c02ddde4` |
| `MacOSRemoteWindowStreamDelegateAssociationCoordinatorTests.cs` | `d4188eb401ef3e12f6f091ba56009f480b7a1a9047dfbb42a72234c0b094a139` |

This original C# implementation covers MSC3/MSC6/MSC9 through a fake native
boundary: retained source identity, an exact nonreused initializer token,
second/third association reads, terminal-only coalescing, source mismatch and
replacement isolation, publication reentry, and unique pending cleanup. Its
independent maximum-16 process ownership records stay charged for unknown
retain/read/release outcomes; uncertain release is not retried. Identity
ambiguity poisons only related construction, while native ownership/resource
faults permanently close global native-work and delivery admission. Known
independent references still receive one cleanup attempt; late uncertainty
cannot reoccupy a completed Capture slot. Router changes are two state-only
operations, with no native calls or external handlers under the state gate.

`CompleteAssociation == true` confirms association, not sharing availability:
its finally cleanup can still close the runtime. Future Capture composition must
consume `Failure` and `NativeAdmissionClosed`, independently of that Boolean.

## Focused tests and honest TDD history

Final preserved stages `17-final-debug-29` and `18-final-release-29` each have
zero-warning/error builds, build/test exit 0 and 62/62 Passed: 29 coordinator
cases plus 33 existing router cases. Result/definition/execution/entry identities
match the saved source and complete inventory; all other counters are zero.
Sorted 62-name inventory SHA-256:
`76b4bcc3fc3209d53ac9617af15f3b2275294bc7adbf7a4301809d26ea5363fb`.

Nine preserved behavior RED→GREEN pairs (`01`, `02`, `04`, `05`, `06`, `07`,
`09`, `10`, `11`) each show one actual failed case followed by the same case
passing and a complete green paired inventory. Twenty coordinator cases were
only observed directly GREEN, including the four review supplements:
publication reentrant rejection; Active passing pending storage; ordinary-first
then nested fatal fault; and late old-callback release uncertainty after slot
reuse. These supplements close the Spec review coverage finding, not new RED
cycles. Standards and Spec reviews have zero remaining findings.

The test method bodies covering the original 25 cases and production files are
unchanged by the four supplements. Final platform DLLs also match the prior matching-configuration
25-case stages. Pair `11` did change three auxiliary read-count expectations
and a fake retained-source predicate; the target failed test body is unchanged.
The audit preserves the exact auxiliary diff rather than claiming whole-test
source equality.

Four compile-only stages and the `13-final-debug-runtimecopy-excluded` stage
without a TRX are excluded from behavior evidence. `01-red` lacks its router
snapshot, original hash manifest and independent build/test exits; its actual
combined log, saved coordinator/test sources, DLLs and failed TRX remain.
Audit-time hashes do not invent those missing historical records. The saved
final runner is not attributed retroactively to earlier stages. All 28 original
stage inventories and the previous 25-case audit copies remain intact.

## Final complete local gates

Run `run-02` on macOS 27.0.1/build 26A434, ordinary arm64, SDK 10.0.301/runtime
10.0.9 passes 24 saved commands, all exit 0 with empty stderr: locked restore,
solution/tool no-change formatting, six zero-warning/error builds, complete
Debug/Release tests, explicit Desktop TEST MODE, protocol-1.7 simulator, four
standalone tools, both Windows portable self-tests and diff check. The
26-project including-transitive advisory query reports no vulnerable packages
or query errors; this is a point-in-time query, not universal security proof.

Each configuration has exactly 12 TRX / 2775 total, executed and Passed,
Completed summaries, zero other counters and no non-Passed result. MacOS has
206 cases and Desktop 760. Complete qualified inventories match; relative to
`974e954` only the 29 coordinator cases are added, with none removed.
Qualified-name inventory SHA-256:
`1d65da43989b932729c943080a549342b8ea9d7d981d092511bcfdb6eae3b88d`.
The serialized inventory JSON has a different, file-byte digest listed below.

All 545 before/after inputs match the preserved base and changed snapshots.
Each configuration preserves 31 top-level runtime files (22 DLLs, seven PDBs,
two JSON); every recorded hash matches and target DLLs match worker stages
17/18. These exclude OS/runtime/native assets and are not a full independently
executable environment or cryptographic compiler attestation.

Existing synthetic and Phase 2a Foundation modes actually pass compatibility
execution: respectively 277-byte and 710-byte one-LF/no-NUL stdout, empty
stderr. Foundation still reports `early_publication_proved=false`. Neither
mode executes the new coordinator against actual Foundation. Windows portable
self-tests report `native_api_called=false`; they are not Windows native runs.
Old `run-01` (25 coordinator cases / 2771 solution cases) remains separate and
cannot substitute for this final 29-case run.

## Preserved reproduction

Focused stage records are in `/tmp/flowspan-msc-coordinator-20261005/`; root
records are in `/tmp/flowspan-msc-coordinator-local-20261005/run-02/`.
The root independently replayed both read-only audits with exit 0 and no errors:

```sh
python3 /tmp/flowspan-msc-coordinator-evidence-audit-20261005.py
python3 /tmp/flowspan-msc-coordinator-run02-gates-audit-20261005.py
```

| Preserved record | SHA-256 |
| --- | --- |
| Focused audit script | `693ed5e0350c41222c9a3ee0c02bc2e362f0a99005b5fef0994a3e7eab0bd82e` |
| Focused audit JSON | `cc2d3374af38e61e8fa31afd3af719447193b19d3cbde46b8842ba95e6892206` |
| Root audit script | `889174a58204a2a32de5c3c955d32e476787267b581bb8db459573ca2ab7b03a` |
| Root audit JSON (66618 bytes) | `5b41d36f7aebd9330998cbf17f6bbb374b6e5f6848e88f92489d76b58fd14335` |
| Root inventory JSON | `bc7131295abde8e6b8c1083c97b8f6cd5b82c8a05b6cb0af533f58f75f497a73` |
| Actual `executed-runner.sh` | `f61f45d592cb1fb1a9a191e32527c9869e5a18d6d7f96f04ad94fde734eac021` |

Audits verify saved provenance and bounded outcomes, not full product acceptance.
The next slice is actual no-capture Foundation early association, followed by
actual Capture composition. Capture remains `delegate=0`; source-loss,
protection/input, independent Emergency Stop, minimum-OS, physical-device,
signing/distribution and release gates remain open. The Goal stays active.
