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
    - [x] Verify this exact implementation checkpoint in hosted CI/CodeQL and
      independently audit downloaded all-OS TRX and native/security artifacts.
      Exact `9deed36` CI failed four Windows cases; macOS/Linux pass 2775 each
      and CodeQL succeeds. All 29 coordinator cases pass everywhere, but complete
      CI and packaging do not. Preserve the
      [failed checkpoint](../../../../docs/evidence/2026-10-05-early-coordinator-hosted-failure.md)
      while repairing each failure; a later success cannot rewrite this run.
      All four failures now have test-only local reproductions and repairs;
      complete Debug/Release solutions pass 2777 each. See
      [fixture evidence](../../../../docs/evidence/2026-10-05-windows-fixture-repairs.md).
      Exact `2c6f8fd` CI/CodeQL now pass; downloaded three-OS inventories each
      contain 12 TRX / 2777 Passed, all 29 coordinator/33 router/10 pairing and
      four ProtectionMutation rows pass. The failed `9deed36` remains failed.
      See [new hosted checkpoint](../../../../docs/evidence/2026-10-05-early-association-hosted-checkpoint.md).
    - [x] Implement and actually execute separate Foundation early-publication
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
      - [x] Download and independently audit fresh exact-commit hosted early
        raw bytes, complete all-OS TRX, CI/CodeQL/security evidence. Local native
        success alone does not close this child or full MSC2b/MSC9.
        Exact `2c6f8fd` hosted early stdout is 1057B, empty stderr, strict step
        successful; 11 small ZIPs match API/upload digests. Root offline replay
        passes. Three large package ZIPs were not downloaded; API/hosted-log
        evidence does not prove their local archive contents.
    - [ ] Exercise production-native unknown-ownership/failure admission and
      full Capture quarantine in task 3b/4. The healthy Foundation proof and
      portable injected faults do not close this remaining MSC2b/MSC9 boundary.
  - Verify permanent bridge, retained numeric tag, independent tag budget,
    callback reference ownership, nil-tag publication race and tag deallocation
    ABI in Debug and Release. Keep no-native defaults and strict evidence gates.
  - _Requirements: MSC1-MSC3, MSC6, MSC8-MSC9_
- [ ] 3. Compose the delegate into the actual candidate Capture.
  - [x] 3a. Add a capture-only native operations/source/completion-owner seam
    and test the same production Capture state machine on all OSes. Preserve
    `delegate=0`, the existing 14.2 candidate floor and production availability;
    this refactor cannot close nonzero-delegate composition. Separate native
    allocation from later configuration so failed setters retain a known owner.
    Verify healthy Start/Stop/Dispose, callback settled versus exited, factory
    rollback/handoff, serial sample barrier, quarantine and sample ownership.
    - [x] Final implementation/local contracts: same Capture and shared sample
      core; final focused Debug/Release 17/17, project 223/223 and complete
      solution 12 TRX/2794 Passed each. Twenty fresh normal-configuration
      processes pass 340 case executions. Static Standards/Spec and independent
      evidence audit have no remaining finding. Four failed no-TRX diagnostics
      remain preserved; no new product assertion-level RED is claimed.
    - [x] Actual existing task-owned native capture regression in Debug/Release:
      `--run` passes selected local macOS checks, with raw bytes/process exits
      and unchanged source/runtime inventories. This is still `delegate=0`,
      not native fault injection or nonzero-delegate proof. See
      [local checkpoint](../../../../docs/evidence/2026-10-05-macos-capture-system-boundary.md).
    - [x] Verify fresh exact-commit all-OS hosted CI/CodeQL and independently
      audit complete downloaded TRX inventories for this source. Exact
      `473c625` has 12 TRX / 2794 Passed on each OS; complete qualified inventories
      match saved local Debug/Release. Eleven downloaded small ZIPs and exact
      native/helper/security evidence pass root's offline replay; three large
      package ZIPs retain API/log-only scope. See
      [hosted checkpoint](../../../../docs/evidence/2026-10-05-capture-boundary-hosted-checkpoint.md).
  - [-] 3b. Compose the process-rooted native delegate/coordinator through that
    same Capture; consume exact initializer and global failure state at every
    construction/Start/sample admission boundary. Require retirement before
    native release and complete cleanup before returning the Capture permit.
    - [x] 3b.1 Fix same-Capture cleanup prerequisites in real RED→GREEN slices:
      no native release under Capture state gates, one attempted/confirmed fact
      per owner, no blind uncertain release retry, independent cleanup and full
      root retention. Reject cached successful Start after a cleanup ownership
      fault. Keep delegate=0/14.2 until later composition evidence.
      - [x] Final local implementation, TDD, complete regression and selected
        actual native regression: ten actual RED rounds / 14 failed executions
        and five direct-GREEN added rows; final 32 focused/238 project cases and
        complete Debug/Release 12 TRX/2809 each, 20 fresh pressure processes/640 Passed.
        Exact-owner index removal preserves replacement/incumbent routing;
        original fatal and durable weak-owner graph are verified. Selected
        task-owned native D/R passes without Skip. Standards/Spec 0/0 and root
        offline audits pass. NativeSource's own algorithms remain task 3b.2. See
        [cleanup evidence](../../../../docs/evidence/2026-10-05-macos-capture-cleanup.md).
      - [x] Verify fresh exact-commit all-OS hosted CI/CodeQL and downloaded
        complete inventories/artifacts; prior `473c625` success cannot close it.
        Exact `a07d911` now passes CI/CodeQL; all three OSes contain matching
        12-TRX/2809-Passed inventories with all 32 Capture rows. Root actually
        replays both downloaded-evidence audits with zero violations. See
        [hosted checkpoint](../../../../docs/evidence/2026-10-05-capture-cleanup-hosted-checkpoint.md).
    - [-] 3b.2 Stage source-acquisition ownership, reserve before native work,
      compose nonzero delegate and exact initializer/global failure admission,
      monotonic Start, managed retirement and complete-cleanup permit return.
      - [x] 3b.2a Fix the real NativeSource acquisition/use/cleanup prerequisite.
        Inject only native retain/release/current-check effects; stage the
        managed retained owner on the same rooted Capture before acquisition.
        Join admitted uses outside gates, reject direct/active-descendant
        self-join, and attempt independently known filter/window cleanup once.
        Unknown partial acquisition or release stays charged without retry.
        Preserve actual RED/GREEN, final local/hosted inventories and selected
        healthy native regression separately. Keep delegate=0/14.2 unchanged.
        CreateSource/enumeration acquisition and catalog-owner rooting remain
        separate unresolved entry points, not implicit acceptance of this slice.
        - [x] Final local implementation, behavior-level TDD, focused/project/
          complete solution Debug and Release, ordinary repeat runs, selected
          task-owned healthy native regression and independent saved-data audits.
          Preserve superseded fixtures and actual failures separately. See
          [source lifecycle evidence](../../../../docs/evidence/2026-10-05-macos-native-source-lifecycle.md).
          Final focused/project D/R pass 59/265 each, complete solution D/R
          each 12 TRX/2836 Passed, ordinary repeat runs 590 Passed. Selected
          actual task-owned native D/R and root saved-data replays pass;
          Standards/Spec have no remaining findings. No nonzero delegate or
          source-entry/catalog ownership acceptance follows.
        - [x] Verify fresh exact-new-commit Windows/macOS/Linux CI and CodeQL,
          downloaded complete inventories and source/artifact bindings. Prior
          a07d911 success is not a result for this implementation.
          Exact `c5c5c52` CI/CodeQL pass; each OS has 12 TRX/2836 Passed
          matching final local D/R and all 27 additions. Root actually replays
          both audits; the independent current review has zero findings. See
          [hosted checkpoint](../../../../docs/evidence/2026-10-05-native-source-hosted-checkpoint.md).
          Three large packages remain API/log-only; native/global/catalog
          obligations and the parent task remain open.
        _Requirements: MSC6-MSC7, MSC9 ownership prerequisite; NR8, NR10_
      - [ ] 3b.2b Compose reserved nonzero delegate/exact initializer, monotonic
        Start, global-fault admission and managed retirement on the same Capture.
        Prerequisite: [source-entry ownership](../macos-source-entry-ownership/tasks.md)
        closes the late catalog-binding failure and pre-reserved durable roots;
        initial native producer acquisition remains subsequent separate work.
        [Initial CreateSource work](../macos-source-producer/tasks.md) has a
        now has locally verified bounded same-record/direct ownership,38
        producer cases and complete D/R2888-case solutions plus new selected
        healthy macOS native D/R. Fresh exact-SHA hosted gates remain pending;
        it does not include enumeration content/Block/callback ownership or
        close this nonzero-delegate composition task.
      - [ ] 3b.2c Implement the bounded process native bridge/tag runtime and
        require complete confirmed owner/root cleanup before permit return.
      - [ ] 3b.2d Verify final exact-source local and fresh all-OS hosted gates;
        portable fault injection is not actual SCStream fault containment.
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
