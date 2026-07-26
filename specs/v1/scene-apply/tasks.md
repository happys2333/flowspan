# Scene Apply Implementation Plan

- [x] 1. Freeze task-8.2 requirements and design
  - Define exact preview/approval binding, deterministic best-effort ordering,
    partial-result truth, Recovering halt, durable replay, and safe compensation.
  - Record the non-atomic orchestration decision in ADR 0017.
  - _Requirements: SA1–SA7_

- [ ] 2. Implement payload-free preview and approval models
  - Add bounded immutable preview, item preparation, confirmation fingerprint,
    result, and reason models.
  - Bind exact user-selected source snapshots, No Change, exact-slot occupancy
    classification, the closed policy/action matrix, and exact Replace
    confirmations without payloads.
  - Add canonical binding digest and invalid/stale/expired/changed-source
    confirmation tests.
  - Freeze preview and Replace-confirmation byte/hash fixtures; cover 1/64/65,
    defensive-copy, canonical-value, malformed-surrogate, and redaction cases.
  - _Requirements: SA1–SA2, SA5_

- [ ] 3. Implement deterministic apply reducer
  - Persist attempt and item boundaries, execute exact saved order, continue
    after proven terminal outcomes, and halt on Recovering/unknown outcomes.
  - Add table/property tests for mixed outcomes, cancellation, replay, and 64-
    item bounds through public interfaces.
  - _Requirements: SA3–SA5_

- [ ] 4. Add protected durable apply journal
  - Add strict bounded state codec, complete-candidate atomic persistence,
    purpose-separated platform key stores, reopen-after-ambiguous-save, and
    restart reduction.
  - Prove payload/title/exception canaries are absent from plaintext and files.
  - _Requirements: SA4, SA7_

- [ ] 5. Route Handoff, Move, and Replace through current production boundaries
  - [x] Add the application-layer `SceneApplyPlanner`, frozen parent/child ID
    source, and narrow read-only preflight port. Prove saved-order exact-ID
    lookup, exact-destination No Change without a slot query, explicit
    multi-source selection with full repreview, per-item unavailable blockers,
    cancellation, and duplicate-ID rejection.
  - [x] Add same-host preflight peers and a direct aggregate port that rechecks
    peer-relative `scene.apply`, discards partial source evidence on any denied
    or unavailable participant, inspects exact-slot occupancy before eligibility
    filtering, and redacts protected/ambiguous occupants.
  - Implement authenticated purpose-scoped exact-Activity-ID source lookup and
    Scene-specific exact-slot occupancy ports; never infer Empty from filtered
    Replace inventory.
  - Require explicit source selection plus full repreview for multiple active
    sources, and perform no child operation for exact-destination No Change.
  - Block occupied Move-plus-Replace before mutation; do not silently preserve
    its source or expose target-only undo that could remove its last instance.
  - Recheck current Trust, additional `scene.apply`, child-operation Capability,
    connection, source, occupancy, exact Replace target, and undo evidence at
    their proper boundaries.
  - Introduce protocol 1.4 strict source-lookup, exact-slot, and payload-free
    remote-child messages. Run a remote selected source locally on that source
    Device; never route its Activity descriptor through the Scene coordinator.
  - Durably deduplicate remote child instructions with the frozen child IDs and
    reduce disconnect/lost acknowledgement/unknown durable state to Recovering.
  - Add same-host authenticated mixed-Scene integration, opaque occupancy,
    independent authorization-denial, heuristic-selection-negative, race, and
    fault tests.
  - _Requirements: SA1–SA5, SA7_

- [ ] 6. Implement explicit safe compensation
  - Record payload-free Undo Capsule references only for committed Preserve-
    Source Replace and invoke exact target-local undo in reverse Scene order
    only on explicit request.
  - Cover stale, expired, consumed, failed, cancelled, and Recovering undo.
  - _Requirements: SA5–SA7_

- [ ] 7. Add Desktop preview, confirmation, and partial-result presentation
  - Show ordered actions/blockers, source disposition, exact Replace targets,
    stale/expiry state, explicit destructive confirmation, and truthful results.
  - Add keyboard, accessible-name, persistent NOT SHARING, and redaction tests.
  - _Requirements: SA1–SA3, SA5, SA7_

- [ ] 8. Close task-8.2 automated evidence
  - Run local full/focused/property/fault/security stress and dual review.
  - Verify implementation and task-status commits on Windows/macOS/Ubuntu,
    Secret Scan, CodeQL, Gitleaks SARIF, and downloaded TRX sums.
  - Keep Scene repository/UI lifecycle (8.3), physical/native, packaging, and v1
    release gates open.
  - _Requirements: SA7_
