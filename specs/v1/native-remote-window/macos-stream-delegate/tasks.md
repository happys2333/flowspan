# Implementation plan

- [x] 1. Implement the portable generation router in vertical RED→GREEN slices.
  - Reserve bounded Capture permits and one nonblocking initializer before
    publication; never reuse a generation or wrap exhaustion.
  - Prove terminal-without-sample, early terminal, unknown/retired-generation
    isolation, publication poison, blocked retirement, self-join, quarantine,
    complete-cleanup permit reuse and failure containment.
  - Preserve actual failed/passing executions and complete local solution gates.
  - _Requirements: MSC2-MSC6, NR8, NR10_
  - Final local focused Debug/Release each pass 33/33; complete solutions each
    pass 2743/2743 across 12 TRX, with all non-success counters zero. Thirteen
    actual RED→GREEN stages have 16 failed cases; final four-lane 40-process
    pressure passes 1320/1320. Standards/Spec review has no remaining findings.
    This closes only portable implementation/local verification. Exact new-SHA
    hosted matrix/CodeQL and all native obligations remain open. See
    [portable evidence](../../../../docs/evidence/2026-10-05-macos-stream-delegate-router.md).
- [ ] 2. Implement and actually execute the no-capture native association proof.
  - Verify permanent bridge, retained numeric tag, independent tag budget,
    callback reference ownership, nil-tag publication race and tag deallocation
    ABI in Debug and Release. Keep no-native defaults and strict evidence gates.
  - _Requirements: MSC1-MSC3, MSC6, MSC8_
- [ ] 3. Compose the delegate into the actual candidate Capture.
  - Separate delegate/output publication; close terminal delivery; prevent
    Start resurrection; retire managed handlers before native release; preserve
    independent sample/Block ownership and complete quarantine.
  - Test actual composition rather than only fake SourceUnavailable delivery.
  - _Requirements: MSC4-MSC7_
- [ ] 4. Execute source-close/failure and exact-commit verification gates.
  - Distinguish native sample callbacks from sink frames. Verify native Stop
    error behavior, fail-close and cleanup without claiming delegate drain.
  - Audit local Debug/Release, all-OS downloaded TRX, CI, CodeQL and evidence.
  - _Requirements: MSC7-MSC8, NR8, NR10_

Completing this candidate slice cannot close parent Task 6, production host
availability, physical-device acceptance, release criteria or the Goal.
