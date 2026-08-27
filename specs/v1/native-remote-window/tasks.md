# Native Remote Window Implementation Plan

- [x] 1. Define the native product and evidence boundary
  - Specify ephemeral Remote Window source identity, exact local binding,
    permissions, capture, input, protection, Emergency Stop, media,
    accessibility, lifecycle, and evidence requirements without inventing an
    Activity Descriptor kind.
  - Preserve the explicit distinction between portable/hosted contracts and
    packaged real-machine proof.
  - _Requirements: NR1-NR10_

- [x] 2. Freeze portable native contracts and source registry
  - Add bounded permission, source catalog, source lease, frame owner,
    protection-source, geometry, and Emergency Stop registration types to
    `Flowspan.Platform`.
  - Refactor the portable controller to consume a bounded Remote Window source
    reference instead of requiring an Activity Descriptor; retain a compatibility
    path for active semantic Activities without adding a descriptor kind or
    changing protocol 1.5 fixtures.
  - Implement an in-memory generation-safe source registry and deterministic
    fakes without native handles crossing the public boundary.
  - Add invariant, stale-generation, callback-after-stop, disposal, and hostile
    bound tests.
  - Evidence: implementation `ff8b7e4`, final lifecycle regression commit
    `5cf76ff`, and
    `docs/evidence/2026-08-20-native-remote-window-portable-contracts.md`.
    Evidence commit `75d1147` passed exact-commit CI `32319985939` and CodeQL
    `32319985831`; native API, physical-device, and tasks 3-10 evidence remain
    open.
  - _Requirements: NR1-NR6, NR8, NR10_

- [x] 3. Compose Remote Window into the production authenticated control session
  - Replace competing Activity and Remote Window read loops with one strict
    connection-owned dispatcher while preserving every frozen protocol fixture.
  - Expose current Remote Window channels from the same peer registration and
    re-check Trust/Capability at each host operation.
  - Add two-node loopback tests for Activity plus Remote Window coexistence,
    revoke/drain, malformed cross-routing, reconnect, and disposal.
  - Evidence: implementation `2f52ae4` and
    `docs/evidence/2026-08-20-native-remote-window-authenticated-control-session.md`.
    Evidence commit `ee371b8` passed exact-commit CI `32361806421` and CodeQL
    `32361806437`; media routing, codec, native API, physical-device, and tasks
    4-10 evidence remain open.
  - _Requirements: NR2, NR4, NR8, NR10_

- [x] 4. Freeze and implement portable media-routing and codec contracts
  - Add protocol 1.6 and a distinct bounded `FSM1` attachment contract that binds
    both Device IDs, one live control route, exact Session and Activity IDs, and
    fresh request/acknowledgement nonces without changing protocol 1.5 fixtures.
  - Implement a bounded, expiring, single-use media-route registry with one
    attachment per transferred control-route media session and fail-closed revoke,
    replay, mismatch, cancellation, and cleanup behavior. Task 5 binds the
    registration lifetime to its production control connection.
  - Pin SkiaSharp 3.119.4 directly and implement the finite JPEG quality/scale
    ladder plus pre-allocation decoder limits of 16,777,216 pixels and 64 MiB.
  - Extend golden decoder/attachment fixtures, downgrade and hostile media tests,
    SBOM, license inventory, package locks, and reproducible package inputs.
  - Keep production Remote Window unavailable until Task 5 composes the listener,
    route registry, encoder, transport, decoder, renderer, and native gates.
  - Evidence: implementation `c269209`, Windows scheduling repair `ec2ccba`,
    and
    `docs/evidence/2026-08-27-native-remote-window-media-contracts.md`.
    Final exact-implementation CI `33086310636` and CodeQL `33086310637`
    passed; production listener/runtime, native API, physical-device, and tasks
    5-10 evidence remain open.
  - _Requirements: NR3, NR8, NR10_

- [ ] 5. Implement the production Desktop Remote Window runtime
  - Project eligible native windows into a dedicated Remote Window source
    inventory and keep them out of semantic and Scene operations.
  - Compose exact source revalidation, permissions, controller, peer endpoint,
    media queues, participant renderer, protection, Emergency Stop, and ordered
    teardown behind `IDesktopRemoteWindowService`.
  - Keep production unavailable until the selected host/participant role has a
    complete native and authenticated path.
  - Add public Desktop tests for source churn, readiness, prompt order, visible
    sharing, rendering, input mapping, failure, and recovery.
  - [x] 5.1 Compose protocol-1.6 media sessions into authenticated control
    registration and the shared published listener. Bind route ownership to the
    control lifetime and stop control on route, attachment, or media failure.
  - [x] 5.2 Implement owned 64-KiB chunking and strict reassembly for a bounded
    1-MiB logical video frame, including mismatch, allocation/copy failure,
    cancellation, replacement, and zeroing tests.
  - [x] 5.3 Implement the capacity-one latest-pending logical-frame sender over
    the peer/session-budgeted outbound queue, with one outstanding wire chunk,
    bounded outcomes, non-cooperative cancellation coverage, and complete
    teardown/failure aggregation.
  - [ ] 5.4 Prove small epoch/frame/plaintext budget exhaustion closes both the
    attachment and owning control connection, then completes a fresh authenticated
    handshake with a new media session and route without rekey or reuse.
  - [ ] 5.5 Compose exact-source capture, permission/readiness, controller,
    JPEG encoder, authenticated media, decoder, participant renderer, protection,
    independent Emergency Stop, visible sharing, input, and ordered Desktop
    teardown. Keep `CreateProduction` unavailable until this path is complete.
  - Candidate evidence for 5.1-5.3:
    `docs/evidence/2026-08-28-native-remote-window-transport-candidate.md`.
    Implementation commit `f430705` has exact-tree local macOS evidence; hosted
    Windows/macOS/Linux CI, Secret Scan, and CodeQL remain required.
  - _Requirements: NR1-NR6, NR8-NR9_

- [ ] 6. Deliver the macOS native vertical slice
  - Implement prompt-free screen-capture and Accessibility facts, explicit TCC
    requests, secure-input observation, exact source enumeration, and generation
    leases through documented CoreGraphics, ApplicationServices, and
    ScreenCaptureKit APIs.
  - Prove the ScreenCaptureKit interop lifetime approach; record an ADR before
    adding a Swift shim if direct managed interop is not maintainable.
  - Implement exact-window capture, bounded frame ownership, CoreGraphics input,
    source/permission loss, and independent local Emergency Stop.
  - Run deterministic tests everywhere, matching-host native smoke on macOS, and
    packaged real-machine grant/deny/revoke/capture/input/protection evidence.
  - _Requirements: NR1-NR10_

- [ ] 7. Deliver the Windows native vertical slice
  - Implement exact-window Windows Graphics Capture, permission/readiness facts,
    SendInput mapping, secure desktop/protected-content uncertainty, source loss,
    and independent local Emergency Stop.
  - Isolate COM/frame-pool ownership and prove late callback, device-loss,
    display-change, lock/unlock, stop, and disposal behavior.
  - Run deterministic tests everywhere, matching-host native smoke on Windows,
    and packaged real-machine grant/deny/revoke/capture/input/protection evidence.
  - _Requirements: NR1-NR10_

- [ ] 8. Deliver Wayland and explicit X11 native slices
  - Implement ScreenCast/RemoteDesktop portal negotiation, PipeWire frame
    ownership, revocation/session-close handling, input mapping, and Emergency
    Stop for the supported Wayland matrix.
  - Implement a separately selected X11 adapter with a persistent security
    degradation and no silent compositor fallback.
  - Run deterministic tests everywhere, matching-host native smoke on Linux, and
    packaged GNOME/KDE Wayland plus documented X11 real-machine evidence.
  - _Requirements: NR1-NR10_

- [ ] 9. Execute cross-platform fault, load, and physical two-device gates
  - Exercise permission loss, source closure, peer restart, network loss,
    sleep/wake, lock/unlock, display change, renderer failure, codec overload,
    Emergency Stop under blocked UI/network, and complete cleanup.
  - Record latency, frame rate, frame drops, memory, CPU, backpressure, reconnect,
    and degradation results for exact signed/notarized package digests.
  - Preserve separate Windows, macOS, Wayland, and X11 evidence records.
  - _Requirements: NR3-NR10_

- [ ] 10. Close parent tasks only from exact evidence
  - Update parent tasks 5.4, 6.1-6.6, 7.2, 7.3, 7.4b, and 9.3 only for proven
    scope.
  - Close affected release criteria only after independent security review,
    packaged native accessibility, physical two-device, and package lifecycle
    evidence all pass at the release commit.
  - Keep task 9.4 and the long-term Goal open until every mandatory criterion is
    checked with reproducible evidence.
  - _Requirements: NR10_
