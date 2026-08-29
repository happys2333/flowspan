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
    Docs SHA `908a04a` subsequently exposed a Linux renderer-tracer sampling
    race in CI `33254082958`: Windows and macOS passed `2234/2234`, while Linux
    passed `2233/2234` and only the renderer `Throw` row observed the host
    session before responder directory publication. Test-only commit
    `ac48ec3aa88aa78f736b5550bc778a5ff4e95abb` explicitly waits for both real
    media-session attachment completions before injecting the three renderer
    failures. Local Debug/Release solutions passed `2234/2234`, the focused
    theory passed `120/120` under eight-way fresh-process pressure, and strict
    review found no P0/P1/P2. Exact-SHA CI `33254883850` and CodeQL
    `33254883851` succeeded; all three hosted OSes passed `2234/2234`, and
    Secret Scan plus all reproducible unsigned package jobs passed. The barrier
    is test-owned and adds no production ordering. Test-only commit
    `58569be3215bbb38a6767398d28c3f428130601a` then adds a fourth renderer row
    that freezes the real listener after initiator acknowledgement and route
    attachment but before host directory publication. It proves participant
    attached/host unattached at failure, Rejected committed before fail-close,
    real host attachment after gate release, zero Admission/capture/send/render,
    and complete cleanup. The final four-row pressure passed `160/160`; local
    Debug/Release solutions passed `2235/2235`, including Desktop `547/547`, and
    strict review found no P0/P1/P2. Exact-SHA CI `33256672974` and CodeQL
    `33256672962` succeeded; every hosted OS passed `2235/2235`, and Secret Scan
    plus all reproducible unsigned package jobs passed. This closes only that
    pre-directory renderer-failure row. The rest of the fault matrix remains
    open, and this closes no Task, native/physical gate, release criterion, or
    Goal.
    Test-only commit `63a52e5e7d2cbba7555a084bc6fa389dba6b5dd9` adds a fifth
    renderer row and thirteenth managed tracer case. It keeps the real listener
    blocked before host directory publication while Rejected returns and
    Start/fail-close/Dispose plus the coordinator/control/directory/route/lease
    cleanup complete while one listener handler remains blocked with
    `ForwardCount == 0`. Gate release then produces the expected stale-owner
    `MediaAttachment` `InvalidDataException`, and a second cleanup check proves
    that handler settles with no resurrection. It creates no replacement
    generation and is not ABA evidence. The TDD RED passed four rows and failed
    only this row after 29 ms;
    final focused Debug/Release passed `5/5`, the tracer passed `13/13`, 40 fresh
    eight-way processes passed `200/200`, and both full solutions passed
    `2236/2236`, including Desktop `548/548` and Transport `701/701`, with
    warning-as-error builds clean and strict review at P0/P1/P2 zero. This is a
    test-only checkpoint, not a production defect fix.
    Full validation exposed two ordering gaps in a pre-existing duplicate-rekey
    test: responder `SendEpoch == 1` sampled after response flush but before its
    local epoch advance, while initiator `SendEpoch == 3` proved the second call
    could start after the first completed. Test-only commit
    `0e573907c30cf34b97339a1dd79ee8d3ca824399` starts both calls before server
    receive and uses a marker returned by that loop as the responder-transition
    barrier. The production send gate already prevents old-epoch application-
    frame interleave; no production source changed, and 200 fresh alternating
    Debug/Release processes passed.
    Exact HEAD CI `33259599324` and CodeQL `33259599282` succeeded. Every hosted
    OS passed `2236/2236`; Secret Scan, CodeQL analysis, and all three
    reproducible unsigned package jobs passed. This closes only the
    fail-close-before-publication row. The remaining fault
    matrix, remaining replacement/ABA evidence, Task 5, Task 5.5a, Task 5.5, all
    native/physical/release gates, and the Goal remain open; `CreateProduction()`
    remains unavailable.
    Test-only commit `ba58562aff020e3cd9fcc5c8066bcfe74d692b8b` adds one
    independent Transport exact-binding replacement-generation contract with no
    production source change and leaves the managed Desktop tracer at thirteen
    cases. The old authenticated generation and published owners drain while one
    accepted attachment stays blocked after route attachment but before directory
    publication. The same Device pair then reconnects with higher generations and
    prepares a replacement route for the same Session and Activity with a fresh
    Route ID, then completes real `FSM1` acceptance so the route is Attached while
    directory publication remains gated. Releasing only the old gate rejects the
    stale exact binding without attaching to, stopping, or consuming the
    replacement. Releasing the replacement gate attaches it and transfers one
    encrypted frame before final zero-owner cleanup.
    A shared-gate fixture RED failed expected 1/actual 2, while removing the
    exact-binding inequality guard made the bounded focused test fail; the final
    two-gate test passed `1/1` Debug/Release, its class `29/29`, 80 fresh Debug
    processes `80/80`, Transport `702/702`, and both solutions `2237/2237`.
    Exact-SHA CI `33261748925` attempt 1 records a macOS exit-137 format failure
    before build/tests/TRX; attempt 2 reran the unchanged SHA and passed
    `2237/2237` on all three hosted OSes, Secret Scan, and all unsigned package
    jobs. CodeQL `33261748927` passed 52 rules with 0 results and 0 open alerts.
    Exact artifacts and digests are in the Transport candidate evidence.
    This closes one Transport row only, not a full Desktop renderer-to-replacement
    trace or the other Session/Activity/Device/reconnect/fault and cleanup-fault
    combinations. The remaining fault and replacement/ABA matrices, Task 5,
    Task 5.5a, Task 5.5, Tasks 6-10, every native/physical/release gate, and the
    Goal remain open; `CreateProduction()` remains unavailable.
    Documentation SHA `124b1a0c8325d7b469702682f8b7f14c1aebfa54` exposed one
    macOS renderer-rejection fixture race in CI `33262767594`. The initiator can
    verify `FSM1` acknowledgement before the responder publishes its attachment
    into the host directory; immediate fixture cleanup then closed the responder's
    still-borrowed stream. A temporary 100-ms delay reproduced the hosted
    exception deterministically. Test-only commit
    `5e5f380393a46021d8106a7f3fa817d3b7ac3765` changes no production source or
    tracer count. Six affected fixtures now assert the exact rejection first,
    then use a bounded cancellable responder-publication barrier before cleanup.
    The temporary probe passed and was removed; production diff returned to empty.
    Local class Debug/Release passed `17/17`, 40 fresh alternating processes
    passed `680/680`, Desktop passed `548/548`, and both solutions passed
    `2237/2237`. Exact-SHA CI `33263840825`, CodeQL `33263840823`, Secret Scan,
    and all three reproducible unsigned package jobs passed. This is a test-
    synchronization repair, not a production ordering guarantee or defect fix.
    Test-only commit `8841080d8cfbfa3714b3cb7c6d858396ceb756b8` changes no
    production source and adds the fourteenth managed tracer case. After real
    protocol 1.7, `FSM1`, Admission, encrypted media, and render, participant
    authenticated-control disconnect starts cleanup. Emergency Stop registration
    disposal first clears its callback and then throws one injected `IOException`.
    The same instance remains observable through `TerminalFailure` and
    coordinator Dispose, while every later capture/input, sharing, renderer,
    protection, permission, media budget, directory, route, handler, channel,
    connection, and current/retained control owner drains. Focused Debug/Release
    passed `1/1`, 80 fresh alternating processes passed `80/80`, the tracer passed
    `14/14`, Desktop passed `549/549`, and both solutions passed `2238/2238`.
    Exact-SHA CI `33264566458` passed `2238/2238` on every hosted OS plus Secret
    Scan and all three reproducible unsigned package jobs; CodeQL `33264566368`
    passed 52 rules with 0 results and 0 alerts.
    This closes one active disconnect by Emergency Stop registration-disposal
    cleanup-fault intersection only. All other cleanup owners/combinations, the
    remaining fault and replacement/ABA matrices, Task 5, Task 5.5a, Task 5.5,
    Tasks 6-10, every native/physical/release gate, and the Goal remain open;
    `CreateProduction()` remains unavailable.
    Test-only commit `6ff3fefaa667e23f309681fe5fe953ae97bb5861` adds the
    fifteenth managed tracer case,
    `RendererFailureLateAttachmentCannotRetargetReplacementDesktopGeneration`,
    without changing production source. Generation 1 completes real
    authenticated TCP, protocol 1.7, and `FSM1`, then blocks its accepted media
    attachment after route attachment but before host-directory publication.
    Renderer Prepare throws, Rejected is observed before fail-close, and the old
    coordinator/control/directory/route/lease graph drains while that handler
    remains gated. The same Device pair reconnects with strictly higher control
    generations and fresh Session, Correlation, and Route IDs. Generation 2 is
    independently blocked at the same publication boundary. Releasing only the
    old gate produces the expected no-live-owner rejection; the replacement
    remains current and pre-Admission with one route and zero capture, send,
    render, or retained driving/controller generation. Releasing only the
    replacement gate then
    completes Applied Admission and one BGRA-to-JPEG-to-encrypted-media-to-decode-
    and-render path before Stop and complete owner drain.
    A deliberately shared-gate fixture was RED after 442 ms, and temporarily
    removing the production exact-binding inequality guard made the focused test
    hit its 30-second bound; the guard was immediately restored. Release-class
    validation exposed the media-attachment handler's Completion-to-Exited
    publication gap; later fresh-process pressure exposed participant renderer-
    disposal publication lag. Explicit bounded barriers repaired both without a
    production change. The final focused fact passed `1/1` in Debug and Release,
    the tracer
    passed `15/15` in each, 160 fresh alternating Debug/Release processes passed
    `160/160`, Desktop passed `550/550`, and both complete solutions passed
    `2239/2239`. Debug and Release warning-as-error builds completed with zero
    warnings and errors; format, diff, direct/transitive vulnerability, explicit
    TEST MODE composition, and simulator checks passed. Exact-SHA CI
    `33266348260` passed `2239/2239` on macOS, Linux, and Windows plus Secret Scan
    and all three reproducible unsigned package jobs. CodeQL `33266348243` passed
    52 rules with 0 results and 0 exact-commit open alerts. Exact artifact IDs and
    digests are in the managed tracer evidence.
    This closes one full managed renderer-failure-to-replacement exact-binding
    trace only. It does not complete the other replacement variants or the full
    reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault cross-product.
    Tasks 5, 5.5a, 5.5, and 6-10, every native/physical/release gate, and the Goal
    remain open; `CreateProduction()` remains unavailable.
    Test-only commit `13681fb451df53290496416d11837ffb5435e500` changes no
    production source and adds the sixteenth managed tracer case by parameterizing
    the active authenticated-disconnect cleanup test. The new capture row clears
    current capture ownership, throws one `IOException`, and proves production
    exposes only bounded `capture=local_boundary_exception` with confirmed input
    and session results, no inner exception, and no injected message. Ordinary
    capture/input Stop then runs once, all remaining managed owners drain, and
    `TerminalFailure` plus the first explicitly observed coordinator
    `DisposeAsync` share one projected failure instance.
    The no-injection RED passed the historical registration row and timed out the
    new row after 20 seconds. Final focused Debug/Release passed `2/2`, 80 fresh
    alternating processes passed both rows at `160/160`, the tracer passed
    `16/16`, Desktop passed `551/551`, both solutions passed `2240/2240`, and all
    local build/format/diff/vulnerability/composition/simulator gates passed.
    Exact-SHA CI `33267557804` passed `2240/2240` on every hosted OS plus Secret
    Scan and all unsigned package jobs; CodeQL `33267557806` passed 52 rules with
    0 results and 0 exact-commit open alerts. At that checkpoint this closed one
    additional cleanup owner only. Other owners/cross-products, the remaining
    fault and replacement matrices, Tasks 5, 5.5a, 5.5, 6-10,
    native/physical/release gates, and the Goal remained open;
    `CreateProduction()` remained unavailable.
    Test-only commit `2c6ff3221c494cd7003ad0a55e91c28e473615da` adds the
    seventeenth managed tracer case and no production change. One authenticated
    disconnect now combines the capture Emergency Stop and registration-disposal
    faults. The final two-inner aggregate preserves the bounded capture projection
    first and exact raw registration exception second; the first explicitly
    observed coordinator `DisposeAsync` shares that outer instance while every
    exact boundary count and owner drain remains satisfied.
    The RED omitted combined registration injection and produced exactly `2/3`,
    with only the new row timing out after 20 seconds. The one-predicate GREEN
    passed focused Debug/Release `3/3`, fresh-process `240/240`, tracer `17/17`,
    Desktop `552/552`, and both solutions `2241/2241`, with every local gate clean.
    Exact-SHA CI `33269125217` passed `2241/2241` on every hosted OS plus Secret
    Scan and unsigned packages; CodeQL `33269125313` passed 52 rules with 0
    results and 0 exact-commit alerts. This closes one combined cross-product
    only. Every other cleanup combination, the remaining matrices, Tasks 5,
    5.5a, 5.5, 6-10, native/physical/release gates, and the Goal remain open;
    `CreateProduction()` remains unavailable.
    Test-only commit `26cd380091f6fd387173e2565023cbb27a96aab0` expands the
    authenticated-disconnect theory from three to five rows and the managed
    tracer from seventeen to nineteen cases, without changing production source.
    The eighteenth row applies input Emergency Stop before one injected throw,
    exposes only the bounded `input=local_boundary_exception` projection with no
    inner/canary, and shares that projected instance through terminal observation
    and the first explicit coordinator disposal. The nineteenth row awaits real
    inner authenticated host-connection disposal, proves the lease non-current,
    then throws once and preserves that raw exception by exact identity through
    the same terminal surfaces. Both rows retain exact boundary counts and drain
    every managed owner.
    Separate REDs produced `3/4` and `4/5`, with only the unconnected new row
    timing out at 20 seconds; the two one-seam GREENs ended at focused Debug/
    Release `5/5`. Final pressure passed `200/200`, tracer `19/19`, Desktop
    `554/554`, and both solutions `2243/2243`; warning-as-error and every local
    gate passed after strict P0/P1/P2 review. Exact-SHA CI `33270854982` passed
    `2243/2243` on every hosted OS plus Secret Scan and all unsigned packages;
    CodeQL `33270854935` passed 52 rules with 0 results and 0 exact-commit open
    alerts. These close two single-owner rows only. All other owners and cross-
    products, the remaining matrices, Tasks 5, 5.5a, 5.5, 6-10, native/physical/
    release gates, and the Goal remain open; `CreateProduction()` remains
    unavailable.
    Test-only commit `5c50870ee11639ee642781e647b135fdd4fc59f7` expands the same
    theory from five to seven rows and the managed tracer from nineteen to twenty-
    one cases, with no production source change. The twentieth row awaits real
    host fail-close before one injected throw and proves the immediate terminal
    path plus CleanupCore share one failure Task and exact exception instance;
    every later owner, including host connection disposal, still drains. The
    twenty-first row combines registration and host-connection disposal faults
    inside one cleanup, producing one flat two-inner aggregate in exact order and
    preserving both exception identities through the first explicitly observed
    coordinator disposal.
    Separate REDs produced `5/6` by a 20-second fail-close-row timeout and a fast
    `6/7` exact-type failure with only registration injection. The two one-seam
    GREENs passed focused Debug/Release `7/7`, fresh-process `280/280`, tracer
    `21/21`, Desktop `556/556`, and both solutions `2245/2245`; warning-as-error
    and every local gate passed, with strict P0/P1/P2 review clean. Exact-SHA CI
    `33271787570` passed `2245/2245` on every hosted OS plus Secret Scan and all
    unsigned packages; CodeQL `33271787616` passed 52 rules with 0 results and 0
    exact-commit open alerts. These close one single-owner row and one cleanup
    cross-product only. All remaining owners/combinations, matrices, Tasks 5,
    5.5a, 5.5, 6-10, native/physical/release gates, and the Goal remain open;
    `CreateProduction()` remains unavailable.
    A 2026-08-30 worktree candidate advances only the H0/H1 pre-Prepare portion
    of `docs/testing/remote-window-production-boundary-matrix.md`. Before route
    selection or Prepare, the host now requires fresh exact-source `Safe`
    protection, revalidates source/connection/permission/grant facts, performs a
    pure non-registering Emergency Stop readiness check, then revalidates those
    host facts again. Exact caller-cancellation barriers separate the synchronous
    boundaries, and the canonical Preparation deadline is rechecked before route
    selection. Permission, authenticated-connection, protection-read, and
    readiness throws reduce to stable reason codes without native exception text.
    Cleanup also joins a fail-close already started by a pre-route safety
    callback and preserves its ordered failure even though no route was selected.
    Local Debug/Release solution verification passes `2286/2286` tests with
    zero build warnings/errors; implementation SHA `eb2e2ad9` is recorded, and
    exact-SHA CI `33275235290` plus CodeQL `33275235305` pass on evidence commit
    `92edfff`.
    These synchronous seams do not prove absolute TOCTOU linearization against
    arbitrary concurrent mutation. `CheckReadiness()` does not reserve the later
    Emergency Stop registration, so an atomic readiness-to-registration
    reservation remains a Task 5.5a blocker. The proposed exact epoch and
    route/send admission contract is recorded in
    `docs/adr/0027-remote-window-host-preparation-reservation.md`. The
    accompanying macOS candidate
    implements only prompt-free CoreGraphics capture-permission preflight and an
    explicit capture-permission request; operation sequencing, observer
    isolation, and disposal tests cover stale concurrent facts and late
    publication; input remains `Unsupported`. It is not
    composed by `CreateProduction()` and is not capture, input, protection,
    physical-device, packaged, signing, or notarization evidence. Scope,
    commands, results, artifact digests, and limitations are recorded in
    `docs/evidence/2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md`.
    Task 5.5a remains unchecked.
    Follow-up implementation `113fce0` advances only P0/AD/HC: policy reasons
    and throws are bounded before connection/renderer ownership; final Admission
    publication preserves exact caller cancellation through the real linked-token
    lease while reducing unexpected/foreign failures to
    `host_admission_publish_failed`; and a 22nd managed tracer case fails after
    participant known-binding publication while frame admission remains closed.
    Focused Desktop rows pass `81/81`, focused lease rows `18/18`, Desktop
    `581/581`, Transport `704/704`, and both solutions `2295/2295` in Debug and
    Release with zero build warnings/errors. Format, diff, vulnerability,
    composition, simulator, and strict review pass. Exact-SHA CI `33277518618`
    and CodeQL `33277518619` pass on evidence commit `158c9a1`; downloaded
    artifacts prove `2295/2295` on each hosted OS, Gitleaks 208/0, and CodeQL
    52/0. P0, AD, HC and Task 5.5a remain partial/open. Scope and limitations are
    recorded in
    `docs/evidence/2026-08-30-participant-policy-and-final-admission-faults.md`.
    Desktop-only implementation `294042fdfcc346e3eade3551d57cc7ccba95c601`
    then adds the proposed host Preparation reservation core without wiring it
    into the coordinator, Platform, Security, Transport, or `CreateProduction()`.
    Its state is `Collecting -> Armed -> RouteAdmitted -> RouteSelected ->
    PrepareSending -> ReadyMatched -> Promoted`, or one irreversible `Terminal`.
    Six opaque fact epochs bind Source, Permission, Authorization, Connection,
    Emergency Stop, and Protection; one bundle can be claimed once. Nine
    deterministic tests cover `M < R`, `R < M < S`, `S < M`, route
    side-effect-then-throw, deadline equality at Arm, route/send admission,
    Ready and promotion, bundle reuse,
    host-generation/fact-epoch ABA, concurrent single-terminal invalidation, and
    exact Ready/promotion phase and binding. Fact invalidation reasons are a fixed
    allowlist rather than caller text.
    TDD REDs included the missing core, incomplete deadline terminal, foreign
    Ready, bundle reuse, late canary throw/leak, and missing Collecting phase.
    Strict review first returned BLOCK with one P1 and two P2 findings; after the
    single-claim, explicit Arm, deadline, and fixed-reason repairs, final review
    returned APPROVE with 0 P0, 0 P1, and 0 P2 findings. Local focused
    Debug/Release passed `9/9`, Desktop Debug/Release `590/590`, and solution
    Debug/Release `2304/2304`; warning-as-error builds had zero warnings/errors,
    and format, diff, vulnerability, composition, and simulator gates passed.
    Hosted exact-SHA execution for `294042f` has not run. Exact commands and
    limitations are in
    `docs/evidence/2026-08-30-host-preparation-reservation-core.md`.
    This isolated core changes no matrix cell and closes neither H0 nor H1.
    Task 5.5a and `CreateProduction()` remain open. The next slice must connect
    real source invalidation, a generation-bound authenticated connection route
    operation, and the actual Transport Prepare send-admission hook.
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
