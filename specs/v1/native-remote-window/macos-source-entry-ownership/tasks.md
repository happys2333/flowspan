# macOS source-entry ownership implementation plan

- [x] 1. Real SourceEntry last-binding failure tracer and correction.
  - Add the thin real-source test factory without changing owner behavior.
  - Actually reproduce late failure allowing another enumeration; preserve
    ordinary/nested-fatal identities, close new admission and do not retry.
  - _Requirements: SCE1, SCE4-SCE5_
- [x] 2. Pre-reserved independent bounded process ownership.
  - Reserve catalog/candidate/batch obligations before enumeration; transfer
    rather than duplicate records, retain full failed entry/batch graphs.
  - Verify shared capacity, pre-effect exhaustion, held binding lifetimes,
    complete healthy return, failed-record retention and allocation-free faults.
  - _Requirements: SCE2-SCE5_

Saved staged evidence: `/tmp/flowspan-source-entry-20261005/` preserves actual
last-binding admission RED (01) followed by minimal GREEN (02), collectible-owner
graph RED (03) followed by bounded-root GREEN (04), and the late-fault preflight
window RED (07) followed by admission recheck GREEN (08). Nested-fatal/independent
cleanup and held-binding/final-confirmed-return additions are direct GREEN
(05/06), not new product RED. Further actual 15→16 and 26→27 failures/corrections
are preserved separately; final v2 adds 14 cases and retains all 265 prior MacOS
identities. See the [final local evidence boundary](../../../../docs/evidence/2026-10-05-macos-source-entry-ownership.md).
The first frozen candidate is superseded after Standards/Spec each find one
issue: production-pool contamination in GC fixtures and skipped known-owner
cleanup in cancellation/overflow drain. Stages 19–24 remain actual historical
passes, not final acceptance. Root run-01's nonzero input check is preserved.

- [x] 3. Final local and selected healthy native verification.
  - Focused/project/solution D/R, format/analyzers/security, identity-preserving
    inventories, ordinary repeats, independent evidence replay and review.
  - Healthy native execution is separate from portable after-effect injection.
  - Final focused/project D/R pass 14/279 each; complete solution D/R each has
    12 TRX/2850 Passed. Ten ordinary CLI invocations pass 140 cases against the
    recorded Debug runtime, not ten fresh builds or independent PID proof.
    Single-layer Standards/Spec repair reviews have no remaining findings.
    New run-02 task-owned healthy native D/R and root saved-data replays pass;
    superseded run-01 and wrong-path replay failures remain recorded.
  - _Requirements: SCE6_
- [ ] 4. Fresh exact-commit all-OS CI/CodeQL and downloaded-evidence audit.
  - Do not inherit c5c5c52 or earlier hosted outcomes.
  - _Requirements: SCE6_

This closes no initial native producer acquisition, MSC9 aggregate, production
sharing, physical-device, release or long-term Goal acceptance by itself.
