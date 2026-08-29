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
  - [x] 5.4 Prove small epoch/frame/plaintext budget exhaustion closes both the
    attachment and owning control connection, then completes a fresh authenticated
    handshake with a new media session and route without rekey or reuse.
  - [ ] 5.5a Freeze and implement the protocol-1.7 Prepare/Ready prerequisite.
    Add independent host-to-participant Prepare and participant-to-host Ready
    messages with exact direction, complete echoed binding, domain-separated
    canonical SHA-256 digest, terminal success/rejection, deadline, one pending
    transaction, and terminal tombstone. Preserve all protocol-1.5/1.6 fixtures.
    Keep participant preparation in an owned worker outside the sole control read
    loop; share one media directory across control and listener; add a
    generation-bound peer connector lease; and consume media plus control on
    every post-route terminal failure. Prove one-way grant direction and that no
    known binding, capture, participant, Driver, frame, or rendering authority
    exists before the exact final Admission state. Add canonical, hostile,
    downgrade, replay, concurrency, deadlock, fault, and complete-cleanup tests.
    This protocol prerequisite alone does not make production available or close
    Task 5, Task 5.5, any native/physical/release gate, or the Goal.
    Local candidate evidence on 2026-08-28 freezes all three protocol-1.7 frames,
    pins the digest known-answer vector, and passes 140 strict control-codec tests
    beside the unchanged protocol-1.5/1.6 fixtures. The 81 focused managed-session
    tests include 62 concurrency cases for deadline wire admission, collision,
    buffered-versus-acknowledged Ready, irreversible completion, Stop
    linearization, and cleanup; additional dispatcher, registration, disposal,
    and generation-lease regressions run outside those focused filters. Desktop
    networking now shares one media directory between the authenticated handler
    and listener and exposes an atomic generation-bound Preparation/media lease.
    Implementation commit `33b39bb` passed exact-commit CI `33135891925` and
    CodeQL `33135891896`: each hosted OS passed all 2096 tests, Secret Scan
    passed, and all three reproducible unsigned-package jobs passed. The verified
    peer-endpoint connector, production coordinator, complementary grant matrix,
    complete production tracer, and native/runtime evidence remain open. Exact
    local results and limits are in
    `docs/evidence/2026-08-28-protocol-1-7-remote-window-preparation.md`.
    Implementation commit `7255f04` also has a deliberately narrow managed
    production-composition tracer over real loopback TCP, authenticated protocol
    1.7, a signed peer endpoint, and the shared `FSM1` listener. Its only tracer
    scenarios are: successful DriverEligible capture/frame/input/Emergency Stop
    cleanup; a reverse-only Mirror-grant rejection; an active authenticated
    control disconnect; and a same-session Mirror capability downgrade that
    terminates and cleans up the host session. This is reproducible exact-commit
    managed loopback evidence on the current macOS host, not proof of the
    complete tracer or of a native or physical-device path. The required
    per-boundary reject, throw, cancel, timeout, revoke, disconnect, and
    cleanup-fault matrix remains open. Therefore Task 5.5a remains unchecked.
    Evidence commit `81b90081265d3d37465557d25406972db2079600` received exact-SHA
    hosted CI run `33155459214` and CodeQL run `33155459192`, both `success`.
    The CI test job IDs are `98797054205` (Linux), `98797054321` (Windows), and
    `98797054461` (macOS); their downloaded artifacts each contain 12 TRX files
    summing to `2190/2190` passed, with every failed/error/timeout/aborted/
    inconclusive/other uncertain TRX counter zero. Secret Scan job `98797054361`
    produced a 208-rule, zero-result SARIF. Reproducible unsigned package jobs
    `98798002805` (osx-arm64), `98798002831` (linux-x64), and `98798002909`
    (win-x64) each succeeded; exact artifact IDs and SHA-256 digests are in
    `docs/evidence/2026-08-28-managed-remote-window-production-tracer.md`.
    Hosted runners remain non-evidence for native API behavior, physical
    two-Device operation, signing, or notarization.
    Follow-up implementation commit `579c9cd` adds managed active
    native-capture permission loss (`Granted` to `Denied`), closes the owning
    authenticated connection, and completes host cleanup. Its exact-SHA CI run
    `33242809777` and CodeQL run `33242809786` both succeeded: Windows, macOS,
    and Linux each passed
    `2192/2192` tests, Secret Scan passed, and all three reproducible unsigned
    package jobs passed. These hosted managed results do not prove native
    permission APIs or packaged permission loss on any operating system.
    Implementation commit `80191d6` extends the managed production-path
    tracer to six scenarios: success, reverse-grant rejection, authenticated
    control disconnect, Mirror-capability revocation, managed native-capture
    permission loss, and a verified-endpoint `FSM1` attachment failure. Commit
    `ca63874` then extends the preparation-only deferred fail-close through an
    accepted TCP connection whose `FSM1` attachment fails, while preserving
    eager control stop for ordinary media connections. Test-only commit
    `761ac75` proves TCP accept before the reset, proves rejection before
    Admission/capture/rendering with complete media/control cleanup, and removes
    a separate outbound-reservation scheduling assumption. Exact-tree local
    macOS Debug and Release solution runs at `761ac75` each passed `2210/2210`,
    including Desktop `535/535` and Transport `688/688`; Debug and Release
    warning-as-error builds, format verification, diff checks, direct/transitive
    dependency vulnerability audit, explicit TEST MODE composition validation,
    and the deterministic protocol-1.7 simulator also passed. This remains
    managed same-host loopback evidence. The complete per-boundary reject,
    throw, cancel, timeout, revoke, disconnect, and cleanup-fault matrix remains
    open.
    Exact-SHA CI run `33246518217` and CodeQL run `33246518202` for `761ac75`
    both succeeded. The downloaded Windows, macOS, and Linux artifacts each
    contain 12 TRX files summing to `2210/2210` passed with every non-success
    counter zero; Secret Scan and all three reproducible unsigned package jobs
    also passed. These remain hosted managed contract and unsigned-package
    results, not native or physical Remote Window evidence.
  - [ ] 5.5 Compose exact-source capture, permission/readiness, controller,
    JPEG encoder, authenticated media, decoder, participant renderer, protection,
    independent Emergency Stop, visible sharing, input, and ordered Desktop
    teardown. Keep `CreateProduction` unavailable until this path is complete.
  - Candidate evidence for 5.1-5.4:
    `docs/evidence/2026-08-28-native-remote-window-transport-candidate.md`.
    Task 5.1-5.3 implementation commit `f430705` and Task 5.4 implementation
    commit `a75afb142c335d8da71e511c29e51b14ad2b3cf7` have exact-tree local macOS
    evidence. The latter passes 460 Transport tests and 131 Security tests in
    both Debug and Release plus all 1,878 Release tests with zero warnings. Its
    four managed-loopback cases cover frame-count/plaintext exhaustion in both
    media directions through the production listener. Exact-commit CI
    `33109385771` passes 1,878/1,878 on Windows, macOS, and Linux plus Secret Scan
    and all three reproducible unsigned package jobs; CodeQL `33109385769` also
    passes. Task 5, Task 5.5, and every native, physical-device, accessibility, and
    packaged-runtime gate remain open.
  - `CreateProduction()` must continue to expose Remote Window as unavailable:
    this candidate does not wire the managed coordinator, native source/capture,
    or participant renderer into the shipped composition root. It cannot be
    treated as a product runtime merely because the managed tracer succeeds.
  - Task 5, Task 5.5a, Task 5.5, tasks 6-10, every native/physical/release gate,
    and the long-term Goal remain open. In particular, local macOS managed
    loopback evidence does not represent Windows or Linux execution, native API
    behavior, two physical devices, package signing, or macOS notarization.
  - _Requirements: NR1-NR6, NR8-NR10_

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
