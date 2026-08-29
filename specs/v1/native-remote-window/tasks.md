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
    Subsequent implementation checkpoint
    `fde38b2bae9d02f177fd86e22a8beecb060325e9` expands that managed tracer from
    six to nine cases. After real authenticated protocol-1.7 control and a
    successful `FSM1` attachment, three parameterized renderer-preparation
    failures exercise a factory throw, a valid null/Missing renderer result, and
    a foreign or tokenless `OperationCanceledException`. Independent assertions
    prove that both media sessions are attached to the exact protocol, Device,
    Session, and Activity binding before the failure. The participant generation
    is synchronously made unavailable before the Rejected response is observed;
    the host observes `renderer_start_failed` for the throw and foreign
    cancellation, or `renderer_unavailable` for null/Missing, before fail-close.
    Admission, capture, media send, and rendering remain at zero, and every
    owner, route, media directory, and control registration drains to zero.
    Actual linked cancellation and deadline expiry retain eager fail-close.
    The response-ordered path uses a request-bound watchdog of at most 10
    seconds that survives lease disposal, is idempotent for the same request,
    cannot be extended by a conflicting request, and is cancelled by owner
    revocation. Expired, overlong, conflicting, or provider-setup-failed
    deferral does not poison the generation; explicit and deadline fail-close
    share one cleanup, with primary, cleanup, and lifecycle failures retained.
    These additions still do not complete the per-boundary reject, throw,
    cancel, timeout, revoke, disconnect, and cleanup-fault matrix.
    Local macOS arm64 verification with .NET SDK 10.0.301 passed warning-as-error
    Debug and Release builds with zero warnings and errors, both complete
    solutions at `2232/2232`, Desktop `544/544`, and Transport `701/701`.
    Ten fresh Debug and ten fresh Release renderer-theory processes passed
    `60/60` case executions; focused connection-lease and media-session suites
    passed `16/16` and `28/28`, respectively, in each configuration. Format,
    diff, direct/transitive vulnerability, explicit TEST MODE composition, and
    simulator checks also passed. No local `gitleaks` result exists. Exact-SHA
    CI `33249181870` and CodeQL `33249181871` for `fde38b2` both succeeded.
    Downloaded Windows, macOS, and Linux artifacts each contain 12 TRX files
    summing to `2232/2232`, with every non-success counter zero; Secret Scan and
    all three reproducible unsigned package jobs also passed. These hosted
    results remain managed contract and packaging evidence, not native or
    physical Remote Window proof.
    Test-only checkpoint `0f1f32d0e8ea251194755a5b4d150d3e294433ff`
    adds no production source change and expands the managed tracer from nine to
    ten cases. `VerifiedFsm1AttachmentThenPreparationExpiryFailsClosedBeforeAdmissionOrCapture`
    completes real authenticated protocol 1.7 with a signed, verified endpoint,
    successful `FSM1` and Ready, one renderer Prepare, and independently proves
    both media sessions are attached to the exact protocol, Device, Session, and
    Activity binding. The test-only coordinator clock is then set exactly to the
    request deadline. Production `EnsurePreparationIsCurrent` treats equality as
    expired and returns the allowlisted `preparation_expired` result. Media
    attachment wait occurs once, while Admission publication, capture, media
    send, and rendering remain zero. Host fail-close and disposal each occur
    once; Snapshot and TerminalFailure are null, and ActiveMediaBudget is null
    because no active generation was published. Renderer, route, directory,
    handler, lease, channel, and control owners drain to zero, and the retired
    generation cannot be reacquired. Local macOS Debug and Release focused runs
    passed `1/1`, the whole tracer class passed `10/10`, warning-as-error builds
    completed with zero warnings and errors, and both complete solutions passed
    `2233/2233`, including Desktop `545/545` and Transport `701/701`. Format,
    diff, dependency-vulnerability, explicit TEST MODE composition, and simulator
    checks passed. Internal strict review reported no P0/P1/P2 finding but is not
    an external audit. Superseding exact SHA
    `e504c839cac2e45a4ca7ad17316c8278e4928c2e`, which contains the expiry test,
    its documentation, and the CI hang-diagnostic guard, passed CI
    `33250747660` and CodeQL `33250747671`. Windows, macOS, and Linux each passed
    `2233/2233` with every non-success TRX counter zero; Secret Scan, CodeQL, and
    all three reproducible unsigned package jobs also passed. These remain
    hosted managed contract and packaging results, not native or physical proof.
    At that expiry checkpoint, only one post-`FSM1`, pre-Admission timeout case
    was added; actual caller cancellation, cleanup-fault coverage, and the full
    per-boundary matrix remained open.
    Test commits `45e2d494501167712ec4abdff69d8d232f355d14` and
    `5bb6d0863033c3b6668335e15d6a6fe336ee46a7` add no production source change and
    expand the tracer to eleven cases with one actual caller-cancellation path.
    After authenticated protocol 1.7, signed endpoint verification, successful
    `FSM1`/Ready, and exact bilateral attachment, a CTS supplied only to
    `StartAsync` is cancelled while the harness CTS keeps connection, run, and
    cleanup alive and the clock remains before the deadline. Production throws
    the cancellation family (observed as `TaskCanceledException`) with the exact
    caller token, not timeout, renderer foreign-fault, or rejection-reason
    reduction. Admission, capture, send, and render remain zero; fail-close and
    Dispose each occur once and all owners drain. Local focused Debug/Release
    runs passed `1/1`, the tracer class passed `11/11`, twenty fresh Debug caller
    processes passed `20/20`, both warning-as-error builds passed, and both
    solutions passed `2234/2234`, including Desktop `546/546`, Platform
    `219/219`, and Transport `701/701`. Other local gates passed and strict
    review reported no P0/P1/P2 finding. Exact-SHA CI `33251741558` and CodeQL
    `33251741546` for `5bb6d08` both succeeded. Windows, macOS, and Linux each
    passed `2234/2234` with every non-success TRX counter zero; Secret Scan and
    all three reproducible unsigned package jobs also passed. These remain
    hosted managed contract and packaging results, not native or physical proof.
    This covers only one post-`FSM1`, pre-Admission
    caller cancellation; cleanup-fault and the full per-boundary matrix remain
    open.
    Docs-only SHA `f300432` later exposed a Windows-only test scheduling failure
    in CI `33252295470`: Linux and macOS passed `2234/2234`, while the Windows
    Desktop project passed `545/546` and failed its five-second bounded check
    for old host control generation retirement. Production already retired
    current before drain; the fixture placed both a synchronously blocked peer
    disconnect and the replacement `Register` on the shared thread pool, so the
    test could poll before replacement started. Test-only commit
    `7b6a6d6796e0280c53eb71755285090c8e19cb5d` moves every blocking host-control
    disconnect, replacement, and external Dispose in that class to dedicated
    threads, adds an explicit replacement-start gate, and removes the tight
    yield loop. Local Debug and Release solutions passed `2234/2234`; 80
    concurrent class runs passed `1200/1200`; and exact-SHA CI `33253258876`
    plus CodeQL `33253258929` succeeded. Each hosted OS passed `2234/2234`, and
    Secret Scan plus all three reproducible unsigned package jobs passed. This
    is test-infrastructure evidence, not a production feature or native/physical
    gate, and it closes none of Tasks 5, 5.5a, 5.5, or 6-10.
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
