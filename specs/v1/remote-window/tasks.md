# Remote Window and Mirror Implementation Plan

- [x] 1. Freeze requirements and control-plane design
  - Define honest fallback semantics, current Capability checks, monotonic lease
    authority, protection fail-closed behavior, local emergency-stop ordering,
    bounded input, and explicit native/media evidence limits.
  - Record the portable control-plane decision in ADR 0021.
  - _Requirements: RW1-RW8_

- [x] 2. Implement the portable live-session control plane
  - Add public bounded sharing snapshots, reason/result types, current
    authorization source, capture/input/session ports, and deterministic fakes.
  - Compose one Activity's `MirrorSession` through start, participant admission,
    role change, Driver transfer, disconnect, expiry, and ordinary stop.
  - Prove every peer use boundary re-reads current `mirror.view`/`mirror.drive`.
  - Current candidate tests prove failed explicit and Capability-driven peer
    disconnects retain retryable cleanup without restoring participant or Driver
    authority, and that active participants plus pending cleanup cannot exceed
    the shared 16-slot session budget; exact candidate evidence remains in task
    9.
  - Historical portable baseline evidence: 69 Platform tests and the complete
    1257-test solution run recorded in
    `docs/evidence/2026-08-08-portable-remote-window-control-plane.md`.
  - _Requirements: RW2-RW4, RW7_

- [x] 3. Integrate protection and local emergency stop
  - Gate every input attempt with a fresh safe protection observation.
  - Pause/blank capture and input on unsafe/unknown/stale state; resume only when
    both local gates confirm a fresh safe state.
  - Revoke authority before synchronous local capture/input/session stop, run all
    boundaries after failures, and expose payload-free confirmation results.
  - Add races for start, input, transfer, protection, and non-cooperative peers.
  - Monotonic protection revisions, bounded re-entrant convergence, late-start
    reconciliation, cancellation-ignoring capture cleanup, Emergency Stop
    dominance, same-generation cumulative boundary confirmations, and
    fail-closed premature reset have deterministic public-API regression
    coverage in the current candidate.
    Exact candidate, native, and physical evidence remains in tasks 6-9.
  - _Requirements: RW4-RW7_

- [x] 4. Freeze authenticated control and bounded media protocol
  - [x] Freeze protocol-1.5 strict messages for session admission, participant
    state, Driver Lease epochs, protection pause, input, and local disconnect.
  - [x] Freeze purpose-separated media framing, backpressure, per-peer/session
    ceilings, rate, timeout, cleanup, and evidence boundaries in ADR 0022.
  - [x] Implement and freeze canonical control frames plus hostile decoding and
    old-minor rejection.
  - [x] Compose the controller through an authenticated two-node loopback.
  - [x] Implement purpose-separated encrypted media plus deterministic queue,
    backpressure, hostile-length/rate, timeout, cancellation, and cleanup tests.
  - Local macOS evidence on 2026-08-08: locked restore, format verification,
    warning-free 26-project Release build, 45 focused Remote Window tests,
    1315 complete solution tests with 12 fresh TRX files, Desktop TEST MODE
    composition, protocol-1.5 simulator, and direct/transitive NuGet
    vulnerability query all passed. Hosted OS and physical evidence remains in
    task 9.
  - _Requirements: RW1-RW8_

- [ ] 5. Implement Desktop Remote Window and Mirror workflow
  - [x] Freeze the bounded Desktop service/view-model boundary, persistent
    safety header, Emergency Stop behavior, permission ordering, and visual
    design in requirements/design.
  - [x] Bind live sharing/Driver/protection state into the persistent header and
    detail band, including Activity/current Driver accessibility naming and
    Driver Lease expiry; implement keyboard Emergency Stop with separate
    local-boundary confirmation and payload-free failure recovery.
  - [x] Add selected-Activity plus purpose-scoped Remote Window target preview
    and explicit view-only/DriverEligible start boundary. Keep `activity.receive`
    semantic targets independent; refilter `mirror.view` / `mirror.drive`
    candidates on preview-role changes and clear ineligible selections. Offer the
    fallback only when the Activity service reports semantic resume unavailable,
    and keep source execution, unavailable adapters, and semantic operations
    distinctly named.
  - [x] Admit `mirror.view` and `mirror.drive` through both production any-of
    control-channel profiles and connector election without treating admission
    as Mirror authority. Prove a mirror-only production loopback reaches the
    view-only inventory, drive-without-view reaches no picker, successful Trust
    mutation refreshes a still-connected inventory, and only removal of the
    final control alternative drains the channel.
  - [x] Add progressive capture permission review/request, then optional
    input/accessibility review/request only when remote driving is enabled.
  - [x] Prove denial/revocation and start races, synchronous permission-stop
    before queued presentation, undefined/read-failed permission reduction,
    command-result snapshot reduction before refresh uncertainty, post-gate
    single-crossing admission, exact stop-confirmed failed-start cleanup/reset,
    stale/new-session and last-known/Unknown state, frozen admitted target,
    duplicate activation, purpose-scoped mirror-only/receive-only/
    drive-without-view inventory negatives, role-upgrade fail-closed selection,
    ignored-cancellation cleanup, inactive-before-success fail-close without
    stopping a newer same-controller session, permission-observer disposal
    without callback self-wait, ordered hostile observer/cancellation/disposal,
    keyboard/focus, screen-reader naming, contrast, and scaling through
    public/headless tests.
  - [ ] Record exact local and hosted evidence without treating fake or hosted
    platform contracts as native permission/capture/input proof.
  - Keep source execution and every unavailable/degraded/native state visible.
  - _Requirements: RW1-RW6, RW8_

- [ ] 6. Implement Windows native adapters
  - Add Windows Graphics Capture, SendInput under current lease, secure-desktop
    and protected-content detection, local emergency hotkey, and matching native
    tests/manual evidence.
  - _Requirements: RW4-RW8_

- [ ] 7. Implement macOS native adapters
  - Add ScreenCaptureKit, Accessibility input, TCC grant/deny/revoke recovery,
    secure-input/protected-window detection, local emergency action, and matching
    native tests/manual evidence.
  - _Requirements: RW4-RW8_

- [ ] 8. Implement Linux native adapters and named degradation
  - Add Wayland portal/PipeWire and RemoteDesktop lifecycles, revocation and
    protection handling, plus explicit X11 capability/security degradation.
  - Add matching native tests/manual evidence across the supported compositor
    matrix.
  - _Requirements: RW1, RW4-RW8_

- [ ] 9. Close Task 6 evidence
  - Run property/fault/security/load tests and exact-commit Windows/macOS/Ubuntu
    CI, Secret Scan, CodeQL, and artifact verification.
  - Portable commit `d19cfea8a06dfec13d298ba2630916dc5e3bbf33`
    passed those hosted gates with parsed artifacts; the exact evidence is in
    `docs/evidence/2026-08-08-portable-remote-window-control-plane.md`.
  - Record physical two-device, native permission/protection, accessibility,
    emergency-stop-under-failure, and real-machine evidence without upgrading
    simulated or hosted claims.
  - Update parent tasks 6.1-6.6 and release criteria only for proven scope.
  - _Requirements: RW8_
