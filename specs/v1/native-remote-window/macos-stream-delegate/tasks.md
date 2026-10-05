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
        The [same-Capture completion prerequisite](../macos-capture-completion/tasks.md)
        also remains unverified: attach the actual one-argument primitive before
        acquisition, separate resource-use join from copy retirement/terminal
        drain, and settle fixed pool ownership before shell-root return. It is
        not process-wide Capture permit composition or nonzero-delegate proof.
        Its [late root-free checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-root-free-progress.md)
        now follows accepted Start/first-Stop portable checkpoints with focused
        D/R112, project D/R381 and actual root replay; these finite behaviors do
        not close the full prerequisite or this parent composition task.
        Its [uncertainty matrix](../../../../docs/evidence/2026-10-05-capture-completion-uncertainty-matrix.md)
        subsequently closes only MCC task3 locally at focused D/R120/project
        D/R389 with actual root replay. Fixed pool/resource-use/outer-drain and
        complete MCC gates remain open; constructor pool task4 is in progress.
        Its [first constructor pool behavior](../../../../docs/evidence/2026-10-05-capture-completion-constructor-pool-progress.md)
        is now accepted locally with actual RED→GREEN and focused D/R121/project
        D/R390/root replay; the remainder of MCC and this parent remain open.
        The later Start/Stop/output-removal healthy-body pool checkpoints are
        also locally accepted, culminating in [focused D/R124](../../../../docs/evidence/2026-10-05-capture-completion-remove-pool-progress.md)
        with both reviews/root replay. Combined faults, acquisition outcomes,
        observers, resource joins and complete MCC still remain open.
        Subsequent finite body/pop combinations culminate in
        [focused D/R127](../../../../docs/evidence/2026-10-05-capture-completion-remove-body-pool-progress.md)
        with actual root replay, preserving unknown-removal debt and independent
        caller cleanup. Pool acquisition/observer outcomes and complete MCC
        still remain open, with no resource-use join/global admission inferred.
        The four zero-token scopes now have separate finite evidence through
        [focused D/R131](../../../../docs/evidence/2026-10-05-capture-completion-remove-zero-pool-progress.md),
        followed by the sequential
        [unknown-push matrix](../../../../docs/evidence/2026-10-05-capture-completion-unknown-push-matrix.md)
        at focused D/R135 with both reviews and root replay. Unissued Stop caller
        cleanup, observer/resource-use/races and complete MCC remain open; the
        last actual project result stays D/R390, with no new hosted acceptance.
        The following [confirmed-unissued Stop caller checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-stop-unissued-caller-progress.md)
        closes that finite single-cleanup RED→GREEN at focused D/R136 with both
        reviews and root replay. Native-invoke/observers, resource-use/races and
        complete MCC remain open; no process/global/native or hosted gate closes.
        The [late-callback pair checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-late-callback-progress.md)
        then passes direct GREEN with unchanged production, focused D/R138,
        both reviews and root replay. Actual late typed callbacks/ABI Joins and
        independent caller/stream-copy releases confirm the finite outcome,
        not resource-use join, pending cross-boundary GC graph proof or full MCC.
        Task4 observers and task5 joins/races remain open; the first task5 tracer
        is underway, and final aggregate/native/new hosted gates are pending.
        The [first active-action repair](../../../../docs/evidence/2026-10-05-capture-completion-active-action-progress.md)
        now passes actual RED→GREEN at focused D/R139, both reviews and root
        replay. The resource-use join prevents premature caller release and
        premature attempt accounting. Only this first task5 behavior closes;
        observer faults, other regions/races/reentry/outer drain and final
        aggregate/native/new hosted plus this MSC parent remain open.
        The [terminal observer-fault matrix](../../../../docs/evidence/2026-10-05-capture-completion-observer-fault-progress.md)
        then passes focused D/R142 (exact139+3), both reviews and root replay.
        Initial Start surfaces its original fatal without rolling back actual
        success; fully confirmed cleanup/lifetime permits known ownership return.
        Action has two actual same-byte REDs; failure/completed are direct GREEN.
        Cached repeated Start, active observers, other task4/task5 work and final
        aggregate/native/hosted plus this MSC parent remain open.
        The [repeated-Start checkpoint](../../../../docs/evidence/2026-10-05-capture-completion-repeated-start-progress.md)
        then passes actual same-byte RED→GREEN, focused D/R143, both reviews and
        root replay. Cached success reports the original recorded fatal without
        another native Start/owner or rewriting confirmed result/handoff facts.
        Active observer joins, ancestry/races/outer drain, final gates and this
        MSC parent remain open.
        The [active completed/failure pair](../../../../docs/evidence/2026-10-06-capture-completion-active-observer-progress.md)
        then passes direct GREEN with unchanged production, focused D/R145,
        both reviews and actual root replay. Published completed/duplicate
        callback return does not permit release while an observer remains active.
        Only those two pending regions close; async/self joins, races, outer
        boundary, final aggregate/native/hosted gates and this MSC parent stay open.
        The [completed ancestry pair](../../../../docs/evidence/2026-10-06-capture-completion-completed-ancestry-progress.md)
        then passes direct GREEN with unchanged production, focused D/R147,
        both reviews and root replay. Direct Dispose and active async descendant
        Stop reject same-Capture self-join before native cleanup. Other ancestry,
        async resource join, races, outer-boundary proof, final gates and this
        MSC parent remain open.
        The [async resource-use join checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-async-resource-join-progress.md)
        then passes actual same-byte RED→GREEN, focused D/R148, both code review
        axes and root replay. Physical drain cannot finish StopAndDrain while
        the guarded completed resource user remains active. Only that finite
        join closes; outer binding, remaining task4/task5, final gates and this
        MSC parent stay open.
        The [outer binding checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-binding-progress.md)
        then passes single D/R direct GREEN and focused D/R149, both code review
        axes and root replay. The actual same-source Catalog/Boundary/Capture
        fallback retains binding/capacity while Stop-copy lifetime is pending.
        This negative contract adds no cleanup behavior repair or recovery proof;
        remaining task4/task5, final gates and this MSC parent remain open.
        The [pending healthy Start checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-pending-start-progress.md)
        then passes direct GREEN with unchanged production, focused D/R150,
        both code review axes and root replay. Dispose preserves pending result
        and admission until the actual callback.
        The [native handoff borrow checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-native-borrow-progress.md)
        then repairs actual Start caller release before the held native frame
        exits: same-test-byte RED→GREEN, focused D/R151, both reviews and root
        replay pass. Fixed finally-exit facts preserve independent pool debt and
        thread affinity. Remaining task4/task5 and this MSC parent stay open.
        The actual [SourceUnavailable observer](../../../../docs/evidence/2026-10-06-capture-completion-unavailable-observer-progress.md)
        and [Stop completed notification](../../../../docs/evidence/2026-10-06-capture-completion-stop-completed-progress.md)
        then pass sequential direct GREEN with unchanged production, focused
        D/R153, both review axes and root replay. Fatal priority and confirmed
        cleanup remain separate from unknown ownership debt. Stop action/
        failure-wrapper proof, remaining task4/task5 and this parent stay open.
        Separate [Stop action](../../../../docs/evidence/2026-10-06-capture-completion-stop-action-progress.md)
        and [Stop failure-observer](../../../../docs/evidence/2026-10-06-capture-completion-stop-failure-observer-progress.md)
        checkpoints now pass direct GREEN with unchanged production, focused
        D/R155, both reviews and root replay/cmp/receipts. Original A, later B
        and successful Stop remain distinct while known cleanup and terminal
        lifetimes permit weak collection. Task4's published-constructor first
        pool-pop-fatal rollback trace, remaining task5/final MCC gates and this
        nonzero-delegate parent remain open.
        The [Start copy-in-flight checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-start-copy-inflight-progress.md)
        then passes direct GREEN, focused D/R156, both reviews and root replay/
        cmp/receipts with unchanged production. Exact pending owner/admission
        survives Stop/Dispose before copy confirmation; actual release/Join
        precedes the original Start success and ordered known cleanup, with
        delivery still closed. Remaining MCC and this MSC parent stay open.
        The [published constructor pop checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-published-constructor-pop-progress.md)
        then passes direct GREEN, focused D/R157, both reviews and root replay/
        cmp/receipts: actual cached async rollback/known cleanup retain unknown
        pool debt and original graph beyond real slot replacement. The sole
        audited task4 gap closes as local finite implementation/test coverage
        only; task5/final MCC gates and this MSC parent remain open.
        The [duplicate Stop checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-duplicate-stop-progress.md)
        now passes unchanged-test-byte RED→GREEN, focused D/R158, both reviews
        and root replay/cmp/receipts. First-result admission and Stop facts share
        one winner while the original completed/native handoff is active;
        actual drain join precedes known cleanup/weak collection. Reverse
        first-error, task5/final MCC and this MSC parent remain open.
        The [duplicate Start checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-duplicate-start-progress.md)
        passes unchanged-test-byte RED→final GREEN, focused D/R159, both reviews
        and root replay/cmp/receipts. Ordinary repeated error cannot close
        successful delivery; real late settlement and independent fatal
        notification remain intact. Closed-late/outer/task5/final MCC and this
        MSC parent remain open.
        The [closed Start late-ABI checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-closed-start-late-progress.md)
        passes direct GREEN, focused D/R160, both reviews and root replay/cmp/
        receipts with production unchanged. Closed use and known native release
        precede actual late entry through a valid extra retain; one known-copy
        retirement permits terminal cleanup without effects replay. Only this
        standalone lifecycle closes; outer recovery/task5/final MCC remain open.
        The [exited completed ancestry checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-exited-ancestry-progress.md)
        passes direct GREEN, focused D/R161, both reviews and root replay/cmp/
        receipts with production unchanged. Actual inherited scope remains
        identical but inactive after parent ABI Join/borrow exit, permitting
        child Stop/Dispose/repeat and full weak collection including that scope.
        Terminal failure publication, outer composition, task5/final MCC and
        this MSC parent remain open.
        The [fresh terminal/outer contained-fatal checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-fresh-terminal-progress.md)
        has actual same-test-byte binding RED→GREEN, final focused D/R162, both
        reviews and root replay/cmp/receipts. Real fresh failure observation
        and complete native cleanup remain separate from preserved diagnosis;
        explicit default-false proof qualifies independent binding return.
        Static OR publication refinement adds no state/native retry/runtime RED.
        Outer healthy/pending-GC recovery, task5/final MCC and this MSC parent
        remain open.
        The [outer healthy checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-healthy-progress.md)
        subsequently passes direct GREEN, focused D/R163, both reviews and root
        replay/cmp/receipts without production changes. Actual binding/source/
        capacity return, same-pool reuse and weak collection close only this
        finite lifecycle; pending-GC recovery, task5/final MCC and MSC stay open.
        The [outer pending-GC checkpoint](../../../../docs/evidence/2026-10-06-capture-completion-outer-pending-gc-progress.md)
        then passes same-complete-test-byte binding RED→GREEN, focused D/R164,
        full macOS project D/R433, both reviews and root replay/cmp/receipts.
        Boundary collects while its original pending graph stays rooted/charged;
        one qualified isolated recovery joins original failed Stop and real
        lifetimes before fresh cleanup proof returns binding/source/capacity.
        No native replay or unknown-effect retry; recorded disposal failures
        remain ineligible. Only ordinary known-held recovery closes, not all
        contained-diagnostic-plus-pending combinations. Final source/aggregate/
        native/hosted, full task5/MCC and this MSC parent remain open.
        The [final source readiness review](../../../../docs/evidence/2026-10-06-capture-completion-final-local-readiness.md)
        then closes finite task5 implementation/test/source coverage, with both
        review axes0. Probe cleanup now retains known references if ABI Join is
        unconfirmed; this is static repair, not runtime RED. Aggregate/native/
        hosted, full MCC and this MSC parent remain open.
        The [final local progress](../../../../docs/evidence/2026-10-06-capture-completion-final-local-progress.md)
        then covers local MCC task6 with new final-source Block and healthy
        task-owned Capture D/R only. Failed aggregate candidate01 stays failed;
        fresh serial candidate02/security/repeats and hosted MCC gates remain
        open. This does not compose nonzero delegate or max16 Capture admission.
        Fresh candidate03 then covers only local MCC task7 with D/R3007,
        20×164 repeats, quality/default security and actual worker/root replay/
        cmp/receipts. Candidate01/02 failures stay preserved. Hosted MCC task8,
        this MSC parent and production/global-admission/v1 scope remain open.
        Prerequisite: [source-entry ownership](../macos-source-entry-ownership/tasks.md)
        closes the late catalog-binding failure and pre-reserved durable roots;
        initial producer acquisition has its separate checkpoint below, while
        finite enumeration ownership is now
        [accepted](../macos-enumeration-producer/tasks.md) with actual composition,
        selected native/healthy SCK and exact `1ab2511` hosted evidence plus its
        separate focused Release closeout. It does not close this nonzero-delegate
        Capture composition task or prove actual SCK fault containment.
        [Initial CreateSource work](../macos-source-producer/tasks.md) now has
        verified bounded same-record/direct ownership,38
        producer cases and complete D/R2888-case solutions plus new selected
        healthy macOS native D/R. Fresh exact `5f62eb8` CI/CodeQL and matching
        three-OS2888 inventories pass with root replay;
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
