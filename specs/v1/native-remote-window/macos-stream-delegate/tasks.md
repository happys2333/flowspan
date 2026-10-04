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
    This closes only portable implementation/local verification. Exact f9896b8
    hosted CI failed in the Windows test job; CodeQL succeeded. Artifact audit
    preserves that failure. The later exact 974e954 hosted checkpoint passes
    the repaired complete matrix; f989 itself remains failed. Neither closes
    native task 2. See
    [portable evidence](../../../../docs/evidence/2026-10-05-macos-stream-delegate-router.md).
- [ ] 2. Implement and actually execute the no-capture native association proof.
  - [x] 2a. Verify ordinary-arm64 Foundation numeric tags, retained callback
    references, old-generation isolation, independent maximum-16 tag budget and
    successful superclass deallocation without Capture. Local Debug/Release
    and 23 independent final native processes pass; strict CI gate and 74 gate
    fixtures are implemented. Exact 974e954 CI/CodeQL and downloaded hosted
    raw proof now pass; see
    [hosted checkpoint](../../../../docs/evidence/2026-10-05-association-hosted-checkpoint.md).
    See [Phase 2a evidence](../../../../docs/evidence/2026-10-05-macos-native-association.md).
  - [ ] 2b. Implement and verify exact-initializer retained early facts, nil-tag
    publication/pending-clear/third-read races, ambiguity and poison/quarantine.
    - [x] Portable coordinator and final local contracts: independent max-16
      ownership records, exact retained source/token, second/third reads,
      terminal-only coalescing, unique cleanup, replacement/reentry isolation,
      unknown ownership quarantine and global resource-fault admission closure.
      Final focused Debug/Release each pass 62/62 (29 coordinator + 33 router);
      complete solutions each pass 2775/2775 across 12 TRX. Nine actual RED→GREEN
      pairs and twenty direct-GREEN cases are separately recorded. Both review
      axes have no remaining findings; see
      [portable coordinator evidence](../../../../docs/evidence/2026-10-05-macos-early-association-coordinator.md).
    - [ ] Verify this exact implementation commit in hosted CI/CodeQL and
      independently audit downloaded all-OS TRX and native/security artifacts.
      Exact `9deed36` CI failed four Windows cases; macOS/Linux pass 2775 each
      and CodeQL succeeds. All 29 coordinator cases pass everywhere, but complete
      CI and packaging do not. Preserve the
      [failed checkpoint](../../../../docs/evidence/2026-10-05-early-coordinator-hosted-failure.md)
      while repairing each failure; a later success cannot rewrite this run.
      All four failures now have test-only local reproductions and repairs;
      complete Debug/Release solutions pass 2777 each. See
      [fixture evidence](../../../../docs/evidence/2026-10-05-windows-fixture-repairs.md).
    - [ ] Implement and actually execute separate Foundation early-publication
      proof. Existing Phase 2a still reports early_publication_proved=false;
      portable fake-boundary tests cannot close this gate or parent 2b.
      - [x] Independent `--run-early-associations` local native tracer: actual
        nil callback, retained same-source pending replay, reference/dealloc
        accounting, controlled second-nil/publication-clear/replacement/third-read
        and fail-closed ambiguity. Final source has 24 fresh early processes,
        identical 1057B stdout/empty stderr; one actual RED→GREEN pair is
        distinguished from direct GREEN and compiler exclusions. No-native
        defaults and old schemas remain unchanged. See
        [native evidence](../../../../docs/evidence/2026-10-05-macos-foundation-early-associations.md).
      - [x] Implement and locally replay strict CI gate: 152 raw-byte fixtures,
        four POSIX CLI fixtures, 12 watchdog contracts and one actual native
        early run pass. Complete solution/root evidence remains independently
        bound; see [local gates](../../../../docs/evidence/2026-10-05-early-checkpoint-local-gates.md).
      - [ ] Download and independently audit fresh exact-commit hosted early
        raw bytes, complete all-OS TRX, CI/CodeQL/security evidence. Local native
        success does not close this child or full MSC2b/MSC9.
  - Verify permanent bridge, retained numeric tag, independent tag budget,
    callback reference ownership, nil-tag publication race and tag deallocation
    ABI in Debug and Release. Keep no-native defaults and strict evidence gates.
  - _Requirements: MSC1-MSC3, MSC6, MSC8-MSC9_
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
