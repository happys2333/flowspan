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
    No workflow ran at the bare implementation SHA `294042f`; hosted evidence
    commit `fa70e63e2dc20f2d617897f5540fc6617e10d4f0` contains that core and
    passes `2304/2304` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and the
    reproducible unsigned packages. Exact commands, artifacts, and limitations
    are in
    `docs/evidence/2026-08-30-host-preparation-reservation-core.md`.
    This isolated core changes no matrix cell and closes neither H0 nor H1.
    Follow-up commit `3d27389de16bcdc43722ac3a94220511f563edb1`
    independently adds the atomic source-invalidation slot, generation-bound
    authenticated responder-route operation, and actual Transport Prepare
    send-admission hook. Exact-SHA CI `33280551919` and CodeQL `33280551900`
    pass `2328/2328` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and all
    unsigned package jobs; that seam-only commit still changes no matrix cell.
    Scope and artifacts are in
    `docs/evidence/2026-08-30-host-preparation-admission-seams.md`.
    Source-composed implementation
    `ec63942296175f63964d8f463335d6b621e22042` then threads the same
    reservation through the coordinator, exact source slot, real authenticated
    route, actual Prepare send gate, Ready match, promotion, and cleanup. Its
    production-composed tracer proves Source `R < M < S`: mutation after the
    owned route prevents Prepare wire delivery and leaves capture, media,
    render, participant policy, and Admission closed while both nodes drain.
    The existing success tracer uses the same reservation path. Focused host
    Debug/Release passes `44/44`, the new tracer `1/1`, Desktop `596/596`, and
    both solutions `2334/2334`; warning-as-error builds and every local gate
    pass with final strict review at 0 P0/P1/P2. Exact-SHA CI `33281547016` and
    CodeQL `33281546949` pass `2334/2334` on every hosted OS, Gitleaks 208/0,
    CodeQL 52/0, and all unsigned packages. Exact commands, artifacts, digests,
    and limits are in
    `docs/evidence/2026-08-30-host-preparation-source-linearization.md`.
    Emergency Stop readiness implementation
    `8e349cc7d9f722caa7e6df404ec6a59117d7d588` next adds a single
    process-local registrar slot that reserves exact owner/Session generations
    before route admission and promotes that same owner to the formal callback
    only after Ready, media attachment, host-fact revalidation, and a fresh
    protection observation. Deterministic Platform and coordinator tests cover
    conflict, release/ABA, loss-versus-promotion, disposal and invalidation
    faults, caller cancellation, promotion side-effect-then-throw, pre-capture
    registration loss, and formal-owner cleanup order. A 24th managed tracer
    case crosses real authenticated loopback route and Transport send admission
    for Emergency Stop `R < M < S`; readiness loss consumes the owned
    connection with zero Prepare wire or later authority and complete owner
    drain. Platform and Desktop pass `239/239` and `608/608`; both solutions
    pass `2355/2355` in Debug and Release, with zero build warnings/errors and
    every local gate green. Exact-SHA CI `33283264188` and CodeQL
    `33283264254` pass `2355/2355` on every hosted OS, Gitleaks 208/0, CodeQL
    52/0, and all unsigned packages. Final strict review is APPROVE with 0
    P0/P1/P2 after two P1 findings and one later P1 finding were repaired.
    Exact commands, jobs, artifacts, digests, and limits are in
    `docs/evidence/2026-08-30-host-emergency-stop-readiness-reservation.md`.
    This proves only a managed process-local registrar and one real-loopback
    order, not an OS hotkey or native action. At that checkpoint the other
    Emergency Stop `M/R/S` orders and fault matrix, production-composed Source
    `M < R` and `S < M`, Permission, Trust, Connection mutation, Protection,
    and the complete per-boundary matrix remained open.
    Exact Trust/Capability implementation
    `635dc23ec0c8f2812d527e16135b3d9c40885788` subsequently binds the
    handshake-proved peer fingerprint from the generation-bound authenticated
    connection lease to one exact Security reservation for Device ID and every
    role-required Capability. ViewOnly requires `mirror.view`; DriverEligible
    requires `mirror.view` plus `mirror.drive`. Applied revoke or Capability
    update—including an Applied same-grant update—deactivates all matching
    Preparation registrations under the Trust mutation gate before ordinary
    observers or active-session Stop. Rejected, thrown, and caller-cancelled
    mutations do not invalidate an uncommitted fact; revoke/regrant, key
    replacement for the same Device ID, and late old disposal cannot revive or
    remove a replacement reservation. Non-fatal sink and Stop failures retain
    stable ordering after commit, while fatal exhaustion escapes unwrapped.
    The Desktop coordinator reserves before route, checks the same owner before
    promotion, releases it after promotion, and owns it through cleanup.
    Focused host plus tracer Debug/Release passes `87/87`, Desktop passes
    `616/616`, and both solutions pass `2377/2377` with zero build warnings or
    errors and every local gate green. A 25th managed tracer case crosses real
    authenticated loopback route and actual Transport send admission for
    Authorization `R < M < S`: an Applied same-grant update invalidates the
    exact reservation, prevents Prepare wire and all later authority, and
    drains both nodes. Exact-SHA CI `33284857461` and CodeQL `33284857449`
    pass `2377/2377` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and all
    version-0.1.195 reproducible unsigned package jobs. Final strict reviews
    report APPROVE with 0 P0/P1/P2. Exact commands, jobs, artifact/package
    digests, and limitations are in
    `docs/evidence/2026-08-30-host-trust-capability-preparation-reservation.md`.
    This closes neither the other production-composed Authorization orders nor
    its complete fault intersections. Permission, authenticated Connection
    mutation, Protection, remaining Source and Emergency Stop orders, and the
    complete per-boundary matrix remain open. Therefore Task 5, Task 5.5a,
    Task 5.5, every native/physical/signing/notarization/release gate, the Goal,
    and `CreateProduction()` all remain open.
    Exact Permission implementation
    `d607ed1c3217c9c4102c4b893d20da9a6845f02d` next adds a synchronous,
    prompt-free reservation for the exact permission owner generation,
    revision, capture/input facts, and frozen role. The macOS boundary
    invalidates current registrations under its accepted-observation commit
    gate before ordinary observers, rejects stale snapshots, preserves
    same-fact revisions, prevents Revoked/Granted ABA, and deactivates every
    registration before surfacing sink or disposal failures. The Desktop host
    owns the registration before route admission, rechecks it before promotion,
    and releases it after promotion or through terminal cleanup. Focused tests
    cover all three host order shapes, exact snapshot/role denial, unavailable
    boundary, ownership-transfer-then-throw, caller and foreign cancellation,
    fatal exhaustion, currentness faults, and release ownership. A 26th managed
    tracer case crosses real authenticated loopback route and actual Transport
    send admission for Permission `R < M < S`: a managed Granted-to-Revoked
    revision prevents Prepare wire and all later authority, regrant cannot
    revive the terminal generation, and both nodes drain.
    Local Platform, macOS Platform, Desktop, and solution Debug/Release pass
    `240/240`, `64/64`, `639/639`, and `2418/2418` respectively; warning-as-
    error builds have zero warnings/errors, format, diff, vulnerability, TEST
    MODE composition, and simulator gates pass, and final review reports no
    P0/P1 finding. Exact-SHA CI `33286525528` and CodeQL `33286525529` pass;
    downloaded artifacts prove `2418/2418` on every hosted OS, Gitleaks 208/0,
    CodeQL 52/0 with 0 open alerts, and all version-0.1.196 reproducible unsigned
    packages. Exact jobs, artifacts, SARIF/package digests, commands, and limits
    are in
    `docs/evidence/2026-08-30-host-permission-preparation-reservation.md`.
    This proves a testable macOS observation-commit gate, not a real TCC revoke;
    macOS input remains Unsupported, Windows/Linux native permission boundaries
    are not implemented, and the managed tracer does not instantiate the macOS
    boundary. Production-composed Permission `M < R` and `S < M`, Connection,
    Protection, all remaining fact orders/fault intersections, and the complete
    matrix remain open. Tasks 5, 5.5a, and 5.5; aggregate H0/H1 acceptance;
    `CreateProduction()`; every native/physical/signing/notarization/release
    gate; and the Goal remain open.
    Exact authenticated Connection implementation
    `259c3bbda4648bc6c45b71d78fbc7a34feb4de71` next replaces point-in-time
    connection reads with one synchronous composite Preparation registration
    committed into both the exact `RemoteWindowConnectionGeneration` and its
    exact `AuthenticatedRemoteWindowMediaSession`. Synchronous owner claim and
    two-slot rollback prevent commit-then-throw leaks; monotonic registration
    identity prevents replacement/late-dispose ABA. Generation revoke, explicit
    or deferred fail-close, media Dispose, control stop, and responder-route
    invalidation all make the old registration terminal under their
    authoritative gate. Authenticated responder-route selection and actual
    Prepare send admission require that exact registration while it is active;
    public, foreign, stale, or omitted owners fail closed.
    The Desktop coordinator reserves before ordinary observers and route,
    rechecks before promotion, and releases after promotion or terminal cleanup.
    Its separate live revocation registration observes generation and media
    mutation through one exact-once callback and overlaps the temporary
    reservation at promotion. Focused host tests cover `M < R`, `R < M < S`,
    and `S < M`, plus conflict, throw/cancellation redaction, exact caller
    cancellation, owner-claim-then-throw, currentness failure, fatal exhaustion,
    release, and cleanup. Transport tests cover both exact slots, route/send
    owner admission, fail-close/cleanup ordering, composite live-callback setup
    and teardown, ABA, and raw fatal cleanup.
    Two additional managed tracer cases bring the class to 28 executions. A
    real authenticated control disconnect after route selection prevents all
    later authority and drains both nodes; it exits before the actual send hook,
    so its zero send-admission count is not claimed as send-gate rejection. A
    separate Transport two-lease `RemoteWindowControlSession` regression proves
    that gate. The second tracer reaches Ready and verified `FSM1` attachment,
    mutates media during the promotion/release handoff, and observes the live
    callback and Emergency Stop before capture, with complete drain.
    Local Transport, focused media-session, Desktop, and solution Debug/Release
    runs pass `755/755`, `41/41`, `654/654`, and `2469/2469` respectively;
    warning-as-error builds have zero warnings/errors, format, diff,
    vulnerability, TEST MODE composition, and simulator gates pass, and two
    independent final reviews report no P0/P1 finding. Exact-SHA CI run
    `33289550263` has completed its Windows, macOS, Linux, and Secret Scan jobs:
    retained 12-TRX artifacts prove `2469/2469` with every non-success counter
    zero on each hosted OS, and Gitleaks reports 208 rules with 0 results.
    Exact-SHA CodeQL run `33289550265` also passes 52 rules with 0 results and
    0 exact-ref open alerts. All three reproducible version-0.1.197 unsigned
    package jobs pass their `SHA256SUMS`, repository verifier, exact commit/
    runtime/unsigned metadata, archive, manifest, and canonical-tree checks.
    Commands, exact jobs/artifacts/digests, and limitations are in
    `docs/evidence/2026-08-30-host-connection-preparation-reservation.md`.
    This is managed same-host loopback and contract evidence, not native or
    physical Windows/macOS/Linux proof. At that exact checkpoint Protection
    still had no exact Preparation reservation, and the remaining production-
    composed Connection orders/fault intersections plus the complete per-
    boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix
    remained open. Tasks 5, 5.5a, and 5.5; aggregate H0/H1 acceptance;
    `CreateProduction()`; every native/physical/signing/notarization/release
    gate; and the Goal remained open.
    Exact native Protection implementation
    `c987ca84e1f9f867f0edef3222a94dc8d25a2583` next binds the complete accepted
    observation identity and payload plus its inclusive freshness interval to
    one synchronous Preparation registration. The host owns that exact object
    before route, and the host reservation rechecks the bound interval through
    actual Prepare send, Ready, and host promotion. The protection registration
    remains `Temporary` through attachment, becomes `FormalPreStart` immediately
    before host promotion, and becomes `Live` only through a fresh post-
    `Starting` source-gate capture decision before source use or native capture.
    Live protection mutation latches under the source gate before Notify and
    ordinary observers, synchronously closes controller Protection admission,
    and enters a bounded FIFO whose non-reentrant callers join their observed
    sequence while active callback ancestry avoids self/cross-boundary
    deadlock. Source loss and overflow fail closed. Each native frame
    destination and native or semantic input call holds one exact
    `ProtectionAdmissionUse`; a current Safe result cannot reopen admission
    until older uses drain and its epoch, observation, lifecycle, and capture
    state still match.
    On implementation tree `c987ca8`, Platform and Desktop Debug/Release pass
    `289/289` and `700/700`; both solution configurations pass `2564/2564` with
    zero build warnings/errors; the focused managed success plus `SecureInput`/
    `Unknown` tracer passes `3/3`; format, diff, vulnerability, TEST MODE
    composition, and simulator gates pass; and two final reviews report no
    P0/P1 finding. Exact evidence/
    test-stabilization tree `457a2c4b9e3d6905218e826cedd60029bbd1b35e`
    then makes one terminal-cleanup assertion deterministic; its focused Release
    row, 50 repeated local executions, and both full `2564/2564` solution
    configurations pass. Exact-SHA CI `33294103546` and CodeQL
    `33294103609` pass; downloaded artifacts prove `2564/2564` on every hosted
    OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref open alerts, and all three
    reproducible version-0.1.200 unsigned packages. Exact commands, jobs,
    artifacts, digests, and limitations are in
    `docs/evidence/2026-08-30-host-protection-preparation-reservation.md`.
    The two new negative managed loopback executions prove only Protection
    `R < M < S`: mutation after a real authenticated protocol-1.7 route enters
    the actual send-admission hook but writes no Prepare and opens no later
    authority, then both nodes drain. They are not native or physical
    Windows/macOS/Linux evidence and do not prove production-composed `M < R`,
    `S < M`, or the complete reject/throw/cancel/timeout/revoke/disconnect/
    cleanup-fault matrix. Therefore Tasks 5, 5.5a, and 5.5; aggregate H0/H1
    acceptance; `CreateProduction()`; every native/physical/signing/
    notarization/release gate; and the Goal remain open.
    Exact commit `8d0831d0716bc68bc1d5dc0ff18c4efc033624b7` adds the 31st
    managed production-composed tracer execution across TX, P0, P2, and CL.
    After Prepare send admission and bilateral verified `FSM1` attachment, a
    deliberately non-cooperative renderer Preparation remains blocked with no
    Ready outcome. Authenticated control disconnect enters owned cleanup and
    cancels the renderer lifetime, but correctly cannot complete before the test
    releases that owner. Release yields one bounded
    `Rejected/preparation_cancelled`, disposes the late renderer, opens no host
    Ready, Admission, capture, media, render, or input authority, and drains both
    nodes. The deliberate RED sentinel failed `0/1`; final focused Debug/Release
    pass `1/1`, twenty fresh processes per configuration pass `20/20`, the
    tracer class passes `31/31`, Desktop passes `701/701`, and both solution
    configurations pass `2565/2565`. Warning-as-error builds have zero warnings/
    errors and format/diff verification passes. Exact-SHA CI `33295825931` and
    CodeQL `33295825897` pass; downloaded artifacts prove `2565/2565` with all
    non-success counters zero on every hosted OS, Gitleaks 208/0, CodeQL 52/0
    with 0 exact-ref open alerts, and three reproducible version-0.1.202 unsigned
    packages whose `5/5` checksums and repository verification pass. Exact jobs,
    artifacts, digests, commands, and limitations are in
    `docs/evidence/2026-08-30-pending-renderer-authenticated-disconnect.md`.
    This changes only P0 Disconnect from M to P. TX, P2, and CL stay P; every
    other cell is unchanged. Tasks 5, 5.5a, and 5.5, `CreateProduction()`, every
    native/physical/signing/notarization/release gate, and the Goal remain open.
    Timeout implementation commit
    `40d4f78f32bb9958c1e7fbc075b6743620d1f0de` adds the 32nd managed
    production-composed tracer execution across TX, P0, P2, and CL. Final CI-
    stabilized evidence tree `de4009aae9b7e5822983e13e70909b7deb8c2b64`
    retains that behavior and hardens the exact shutdown classifier plus local-
    pairing publication lifetime.
    Separate manual clocks advance only the participant to exact deadline
    equality after Prepare send admission and bilateral `FSM1` attachment. Host
    time remains earlier and peer disconnect has not entered when the blocked
    renderer token is cancelled. Release yields one bounded
    `Rejected/preparation_expired`, then disconnect; no Ready authority,
    Admission, capture, media, render, or input opens, and the late renderer plus
    both nodes drain. This changes only P2 Timeout from M to P. CL Timeout stays M
    because cleanup itself does not time out; every other cell is unchanged.
    Final local results pass focused `2/2`, fresh deadline Debug/Release `10/10`
    each, tracer `32/32`, Desktop `707/707`, solution `2571/2571`, zero build
    warnings/errors, and format/diff gates. Three strict review rounds report
    zero P0/P1/P2.
    Earlier tree `c761acf` CI `33296383742` failed Windows job `99216650548`
    on an exact stale-aggregate classification gap and `Task.Run` publication
    starvation, so it is not success evidence; CodeQL `33296383740` succeeded
    independently. CI `33297152942` and CodeQL `33297152906` pass for
    `40d4f78`. Final exact-SHA CI `33298564630` and CodeQL `33298564676`
    pass for `de4009a`; downloaded artifacts prove `2571/2571` with all non-
    success counters zero on every hosted OS, Gitleaks 208/0, CodeQL 52/0 with
    0 exact-ref open alerts, and three reproducible version-0.1.205 unsigned
    packages whose `5/5` checksums and repository verification pass. Exact jobs,
    artifacts, digests, scope, commands, run history, and limitations are in
    `docs/evidence/2026-08-30-pending-renderer-deadline.md`.
    Tasks 5, 5.5a, and 5.5, `CreateProduction()`, every native/physical/signing/
    notarization/release gate, and the Goal remain open.
    Participant current-lease ownership implementation
    `681c0f72b4f584aba8fa6bf7e915a27317636ff9` next closes one P0
    acquisition side-effect hole. If the current-connection collaborator assigns
    a real generation-bound `out` lease and then throws, the participant peer now
    attaches that lease to its generation in a `finally` before the exception is
    classified or propagated. A non-fatal `IOException` produces bounded
    `Rejected/media_unavailable`, invokes no renderer, releases the lease, and
    permits an ABA-safe replacement; an exact fatal `OutOfMemoryException`
    escapes unchanged while the lease remains owned until terminal cleanup.
    Final local tree `213327c4373379f5f92457ab651741dd5bdd85c4` passes the
    focused rows `2/2`, twenty fresh processes per configuration and `80/80`
    total invocations, Desktop `709/709`, solution `2573/2573`, and the unchanged
    managed tracer `32/32`, all in Debug and Release. Builds have zero warnings/
    errors and format/diff checks pass. Exact hosted workflow, artifact, digest,
    static-analysis, package, and limitation evidence is in
    `docs/evidence/2026-08-30-participant-lease-acquisition-ownership.md`.
    Final exact-SHA CI `33300966551` and CodeQL `33300966509` pass for
    `15aba95`; downloaded artifacts prove `2575/2575` on each hosted OS,
    Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref alerts, and three reproducible
    version-0.1.209 unsigned packages whose `5/5` checksums and repository
    verification pass.
    P0 Throw was already P and remains P, no 33rd tracer case is added, and every
    other matrix cell is unchanged. Current Trust/lease revocation, the other
    authenticated-disconnect phases, and remaining cleanup-fault intersections
    stay open. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
    `CreateProduction()`, every native/physical/signing/notarization/release gate,
    and the Goal remain open.
    Exact test-only commit `8413d065ba7f9d2d2b05e8b52d9c97eace768cf9`
    adds the 33rd production-composed managed tracer case for participant Trust
    revoke while renderer Preparation is blocked. It uses a real signed/verified
    candidate,
    `AuthenticatedTcpPeerSessionAttempt`, `SystemAuthenticatedTcpConnector`, and
    the production Trust store/session ownership path. After exact Prepare send
    admission, bilateral `FSM1`, and acquisition of the current participant
    lease, `participantTrust.RevokePeerAsync` removes Trust, invalidates and
    prevents reacquisition of that lease, enters peer-disconnect cleanup, and
    cancels renderer lifetime. Before explicit renderer release, revocation,
    session attempt, disconnect cleanup, and Preparation remain incomplete while
    Ready, Admission, capture, media, render, and input stay zero. Release yields
    bounded `Rejected/preparation_cancelled`, disposes the late renderer, returns
    revoke `true`, ends the attempt as `PermanentRejection/PeerNotTrusted`, and
    drains both nodes. Focused Debug/Release passes `1/1`, ten fresh processes
    per configuration pass `10/10`, tracer Debug/Release passes `33/33`, Desktop
    Debug/Release passes `710/710`, solution Debug/Release passes `2574/2574`,
    both warning-as-error builds have zero warnings/errors, and format/diff gates
    pass. Immediate parent `d89758b` hardens only the previously starved Windows
    test fixture. Final exact-SHA CI `33300966551` and CodeQL `33300966509` pass
    for `15aba95`; retained artifacts prove `2575/2575` on each hosted OS,
    Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref alerts, and three reproducible
    version-0.1.209 unsigned packages. Commands, jobs, digests, scope, and
    limitations are in
    `docs/evidence/2026-08-30-pending-renderer-trust-revoke.md`.
    By fault origin P0 Revoke and P2 Revoke change M→P. TX Revoke and CL Revoke
    are strengthened but remain P; every other cell is unchanged. Tasks 5,
    5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every
    native/physical/signing/notarization/release gate, and the Goal remain open.
    Exact test-only commit `15aba95409c62d858669b740957da54a5bce6b95`
    adds the 34th production-composed managed tracer row at final Admission.
    After the participant has committed and published exact `Applied` or
    `AlreadyApplied`, but before host post-publication revalidation and
    `Admission.TryOpen()`, real fingerprint-bound
    `hostTrust.UpdateCapabilitiesAsync` replaces `mirror.view` with no
    Capabilities. The old inbound connection becomes non-current and cannot be
    reacquired. Although capture has started once, its initial pre-Admission
    frame and a second frame deliberately emitted in the boundary hook are each
    disposed; host failure is bounded to `authenticated_connection_stale`;
    capture/input Emergency Stop locally; media send/render/input remain zero;
    and both nodes drain. The focused pair passes `2/2`; ten fresh final-row
    processes pass `10/10` per configuration; tracer, Desktop, and solution pass
    `34/34`, `711/711`, and `2575/2575` in Debug and Release; both builds have
    zero warnings/errors; format/diff checks pass; and independent strict review
    is APPROVE with 0 P0/P1/P2 findings. Final exact-SHA CI `33300966551` and
    CodeQL `33300966509` pass; artifacts prove `2575/2575` with every non-
    success counter zero on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0
    exact-ref alerts, and three reproducible version-0.1.209 unsigned packages.
    Commands, jobs, digests, scope, and limitations are in
    `docs/evidence/2026-08-30-final-admission-authority-revoke.md`.
    By fault origin only AD Revoke changes M→P. HC Revoke remains M, CL Revoke
    remains P, and every other cell is unchanged. Tasks 5, 5.5a, and 5.5,
    aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
    signing/notarization/release gate, and the Goal remain open.
    Exact test-only commit `7be177bb010c55ba44c852a851b60c3ba843d9d7`
    adds the 35th production-composed managed tracer row at final Admission.
    After participant exact `Applied` or `AlreadyApplied`, but before host post-
    publication revalidation and frame-gate open, the hook starts real
    `participantConnection.DisposeAsync()` without changing Trust, fingerprint,
    or the sole `mirror.view` grant. It waits only for a post-host-revocation-
    callback barrier, not full disposal; the old generation is then non-current
    and unreacquirable. The initial frame is disposed, and the hook-emitted frame
    owner disposes exactly once; host failure is bounded to
    `authenticated_connection_stale` with no inner/fingerprint; capture/input
    Emergency Stop locally; media send/render/input remain zero; and outside-
    hook disconnect/session joins drain both nodes. Focused Debug/Release passes
    `3/3`; ten fresh final disconnect rows
    pass `10/10` per configuration; tracer, Desktop, and solution pass `35/35`,
    `712/712`, and `2576/2576`; both builds have zero warnings/errors; format/
    diff checks pass; and self plus independent review report 0 P0/P1/P2.
    Final exact-SHA CI `33302056214` and CodeQL `33302056182` pass for evidence
    tree `629d1e5`; artifacts prove `2576/2576` with every non-success counter
    zero on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref alerts,
    and three reproducible version-0.1.211 unsigned packages whose `5/5`
    checksums and repository verification pass. Earlier CI `33301715578` and
    CodeQL `33301715584` target `c13acc5` and are not evidence for this row.
    Commands, jobs, digests, scope, and limitations are in
    `docs/evidence/2026-08-30-final-admission-authenticated-disconnect.md`.
    By fault origin only AD Disconnect changes M→P. HC Disconnect remains M, CL
    Disconnect remains P, and every other cell is unchanged. Tasks 5, 5.5a, and
    5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
    signing/notarization/release gate, and the Goal remain open.
    Exact implementation/fix commit
    `fe0be79e0accbbb0cd4eef27b62e12620a18eccf` adds the 36th managed
    tracer at HC capture Start. Ready and bilateral `FSM1` exist, capture's first
    frame owner disposes exactly once, and Start has not returned when real
    participant disconnect fires. The post-host-revocation callback barrier
    proves the old generation non-current/unreacquirable while Trust, fingerprint,
    and `mirror.view` remain unchanged. Admission publish/send/render/input stay
    zero; failure is causal `authenticated_connection_stale` with no inner/
    fingerprint; capture/input Emergency Stop; and full disconnect/session joins
    drain both nodes.
    The exact RED expected stale but observed `emergency_stop_won_start_race`.
    One post-Start `ValidateCurrentHostFacts` before Start-result projection is
    the minimal production fix; GREEN passes. The same-generation media-mutation
    row now expects causal stale rather than `session_not_idle` after
    `RequestControlStop`. Focused Debug/Release passes `1/1`; fresh rows pass
    `10/10` per configuration; tracer, Desktop, and solution pass `36/36`,
    `713/713`, and `2577/2577`; builds have zero warnings/errors; format/diff
    checks pass; and self plus two independent reviews report 0 P0/P1/P2.
    Final exact-SHA CI `33303210427` and CodeQL `33303210391` pass for evidence
    tree `a0c9648`; artifacts prove `2577/2577` with every non-success counter
    zero on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref alerts,
    and three reproducible version-0.1.213 unsigned packages whose `5/5`
    checksums and repository verification pass. Earlier CI `33302708813` and
    CodeQL `33302708801` target `17a3401` and are not evidence. Commands, jobs,
    digests, scope, and limitations are in
    `docs/evidence/2026-08-30-host-capture-start-authenticated-disconnect.md`.
    By fault origin only HC Disconnect changes M→P. AD Disconnect and CL
    Disconnect remain P; every other cell is unchanged. Tasks 5, 5.5a, and 5.5,
    aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
    signing/notarization/release gate, and the Goal remain open.
    Exact test-only commit `62e9372aef378e8c085ccf79502104f63ae8aa76`
    adds the 37th managed tracer using the same HC capture-start runner. The hook
    applies real fingerprint-bound `hostTrust.UpdateCapabilitiesAsync` with
    `CapabilityGrant.None`; the mutation is Applied and reaches the callback
    barrier. Trust identity/fingerprint remain, Mirror authority is empty, the
    old generation is non-current/unreacquirable, and Ready/bilateral `FSM1`
    coexist with exact first-frame disposal and zero Admission/send/render/input.
    The existing `fe0be79` revalidation produces causal stale with no inner/
    fingerprint; Emergency Stop/full drain pass without another production
    change. Focused Debug/Release passes `1/1`; fresh rows pass `10/10` per
    configuration; tracer, Desktop, and solution pass `37/37`, `714/714`, and
    `2578/2578`; builds have zero warnings/errors; format/diff checks pass; and
    self plus independent review report 0 P0/P1/P2. Final cumulative evidence
    tree `c4c02a3` contains this row, the pairing fix, and rows through 39; CI
    `33305006486` and CodeQL `33305006421` succeed with `2580/2580` on every
    hosted OS, Gitleaks 208/0, CodeQL 52/0, and three verified reproducible
    unsigned packages. Commands, scope, and limitations are in
    `docs/evidence/2026-08-30-host-capture-start-authority-revoke.md`.
    By fault origin only HC Revoke changes M→P. HC/AD/CL Disconnect remain P;
    every other cell is unchanged. Tasks 5, 5.5a, and 5.5, aggregate H0/H1
    acceptance, `CreateProduction()`, every native/physical/signing/notarization/
    release gate, and the Goal remain open.
    Exact test-only commit `0f26c26e93c0af6013372245ba448fd839037a1c`
    adds the 38th managed tracer at HC capture Start. A dedicated token belongs
    only to Start while a separate 20-second harness token bounds network/join
    work. After Ready, bilateral `FSM1`, and exact first-frame disposal, the hook
    synchronously cancels the caller. Admission/send/render/input remain zero;
    the connection remains current; an exact probe acquires the same generation
    and immediately releases it; Trust/fingerprint/sole `mirror.view` remain
    unchanged. Before rethrowing exact-caller-token
    `OperationCanceledException`, Host Start awaits its owned ordinary Stop,
    fail-close, connection disposal, and cleanup. The test then joins participant
    session completion and verifies both nodes drained. No production change is
    required. Focused Debug/Release passes `1/1`;
    fresh rows pass `10/10` per configuration; tracer, Desktop, and solution pass
    `38/38`, `715/715`, and `2579/2579`; builds have zero warnings/errors;
    format/diff checks pass; and self plus independent review report 0 P0/P1/P2.
    Final cumulative hosted evidence is the successful `c4c02a3` tree and
    `33305006486`/`33305006421` runs described above; the earlier `9ca4b2c` runs
    precede this row. Commands, scope, and limitations are in
    `docs/evidence/2026-08-30-host-capture-start-caller-cancellation.md`.
    By fault origin only HC Cancel changes M→P. CL Cancel remains P; every other
    cell is unchanged. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
    `CreateProduction()`, every native/physical/signing/notarization/release gate,
    and the Goal remain open.
    CI `33304022418` at `9ca4b2c` then failed only macOS local-pairing lifetime
    cancellation precedence: a cancellation-ignoring late Enable exposed
    `ObjectDisposedException` instead of `OperationCanceledException`. Ubuntu,
    Windows, and Secret Scan passed; packages were skipped; CodeQL `33304022374`
    passed independently. Production fix
    `72394484e9fd0fd556497641f1ac5d79afe80bce` checks lifetime
    cancellation before linked cancellation/disposal. Pairing runtime passes
    `29/29` in Debug/Release and the exact row passes twenty fresh processes per
    configuration. This changes no Remote Window matrix cell. Details are in
    `docs/evidence/2026-08-30-local-pairing-lifetime-cancellation.md`.
    Exact test-only commit `858acb2c28321ed8603646227d8834eef318405a`
    adds the 39th managed tracer at HC capture Start. After Ready, bilateral
    `FSM1`, exact first-frame disposal, and a same-generation currentness probe,
    capture returns bounded `capture_start_failed`; Trust/transport remain
    unchanged and Admission/send/render/input remain zero. Host failure has no
    inner/fingerprint; ordinary capture/input Stop runs with exact Emergency Stop
    counts zero; and both nodes drain. No production change is required. Focused
    Debug/Release passes `1/1`; fresh rows pass `10/10` per configuration;
    tracer, Desktop, and combined solution pass `39/39`, `716/716`, and
    `2580/2580`; builds have zero warnings/errors; format/diff checks pass; and
    self plus independent review report 0 P0/P1/P2. Final cumulative evidence
    tree `c4c02a3` contains `858acb2` and pairing fix `7239448`; exact-SHA CI
    `33305006486` and CodeQL `33305006421` succeed with the hosted results named
    above. Commands, scope, and limitations are in
    `docs/evidence/2026-08-30-host-capture-start-rejection.md`.
    By fault origin only HC Reject changes M→P. HC Reject, Cancel, Revoke, and
    Disconnect are now P; every other cell is unchanged. Tasks 5, 5.5a, and 5.5,
    aggregate H0/H1 acceptance, `CreateProduction()`, every native/physical/
    signing/notarization/release gate, and the Goal remain open.
    Exact test-only commit `077c996e82dd4077d24a58957c37b86383479f6e`
    adds the 40th managed tracer at H0. Real authenticated protocol-1.7 loopback
    blocks after a fingerprint-bound Trust Authorization reservation is acquired
    inside a wrapper but before it is returned to the coordinator. At the frozen
    barrier the exact Connection Preparation registration and live callback are
    current; H1 Protection, Emergency Stop, route, Prepare, capture, Admission,
    media attachment/send, render, and input authority are unopened. Independent
    participant
    Connection disposal reaches the real host callback: Connection and its
    Preparation registration become non-current and cannot be reacquired, while
    the wrapper-owned Authorization registration and unchanged
    Trust/fingerprint/sole `mirror.view` grant remain current. Releasing the
    barrier transfers Authorization ownership to the coordinator, which
    disposes it and returns bounded `authenticated_connection_stale`. No route
    exists, so fail-close is 0, Connection disposal is exactly 1, every
    downstream Prepare/capture/Admission/media-send/render/input count stays
    zero, and both nodes fully drain while Trust and the exact source lease
    remain current.
    No production change is required. Focused Debug/Release passes `1/1`; fresh
    rows pass `10/10` per configuration; tracer, Desktop, and combined solution
    pass `40/40`, `717/717`, and `2581/2581`; builds have zero warnings/errors;
    and format/diff checks pass. Exact-SHA CI `33305848081` and CodeQL
    `33305848085` succeed; downloaded artifacts prove `2581/2581` with every
    non-success counter zero on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with
    0 exact-ref open alerts, and three verified reproducible unsigned packages.
    This remains managed/contract evidence, not native API, physical two-Device,
    signed/notarized package, or release proof.
    Commands, scope, ownership semantics, and limitations are in
    `docs/evidence/2026-08-30-host-initial-authorization-disconnect.md`.
    By fault origin only H0 Disconnect changes M→P. H1 Disconnect remains M,
    CL Disconnect remains P, and every other cell is unchanged. Tasks 5, 5.5a,
    and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/
    physical/signing/notarization/release gate, and the Goal remain open.
    Exact commit `d5931817d95b592bfa4e22eb8da304a18c86e2ca` adds the
    41st managed tracer and the post-route authority gate. Real authenticated
    protocol-1.7 loopback completes one inner responder-route side effect before
    the hook: the host route exists, exact Connection Preparation is current,
    Protection is reserved, Emergency Stop readiness is current, and Prepare has
    not been called. Independent participant Connection disposal reaches a
    barrier only after the production callback returns; Connection becomes
    non-current and unreacquirable while Trust/fingerprint/sole `mirror.view`
    remain unchanged. Host Start returns bounded
    `authenticated_connection_stale`; Prepare call/wire, attachment, capture,
    Admission, media send, render, and input remain zero. Owned route fail-close
    and Connection disposal each run once, and both nodes fully drain.
    The exact RED expected `PrepareCount` 0 but observed 1. A minimal post-route
    fact read first made the row GREEN; strict counter-review then required the
    final gate to order caller cancellation → terminal cause → deadline → current
    host facts plus fresh-safe Protection → repeated cancellation/terminal/
    deadline. Concurrent non-fatal failure retains the recorded terminal cause;
    OOM remains the exact primary entering the existing outer cleanup/
    aggregation path. Focused H1 passes
    `1/1`; H1 plus coordinator passes `116/116`; tracer, Desktop, and solution
    pass `41/41`, `718/718`, and `2582/2582` in Debug/Release; builds have zero
    warnings/errors, and format/diff checks pass. CI `33306962398` failed only
    macOS Transport
    `ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler(Omit)`:
    macOS passed `2581/2582` overall and Desktop `718/718`; Ubuntu and Windows
    each passed `2582/2582`; Secret Scan passed 208/0; CodeQL `33306962391`
    passed 52/0; packages were skipped. Test-only `c98a570` widens only that
    theory's handshake/failure/outer budgets from 300 ms/2 s/3 s to 2 s/4 s/6 s
    with assertions unchanged. Focused Debug/Release passes `3/3`, ten fresh
    Release processes pass `30/30`, Transport passes `755/755`, and strict
    review reports APPROVE. Exact-SHA CI `33307322868` and CodeQL `33307322870`
    then succeed: downloaded artifacts prove `2582/2582` with every non-success
    counter zero on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref
    open alerts, and all three reproducible unsigned packages verify. Commands,
    scope, and limitations are in
    `docs/evidence/2026-08-30-host-route-authenticated-disconnect.md`.
    By fault origin only H1 Disconnect changes M→P. H0 and CL Disconnect remain
    P, and every other cell is unchanged. The local tracer is same-host managed
    macOS evidence and the hosted matrix remains managed/contract evidence;
    neither is native API, physical two-Device, signed/notarized package, or
    release proof. Tasks 5, 5.5a, and 5.5,
    aggregate H0/H1 acceptance, every native/physical/signing/notarization/
    release gate, and the Goal remain open. `CreateProduction()` remains
    unavailable.
    - [x] 5.5a.1 Freeze bounded cleanup-confirmation semantics in ADR 0028 and
      NR8.9-NR8.17. Separate one complete real termination task from one bounded
      confirmation operation; retain retiring ownership after timeout; define
      the sticky restart latch, deterministic failure ledger, fatal OOM lane,
      timer failure, Dispose immutability, and final pre-generation scope.
      _Requirements: NR8.9-NR8.17_
    - [x] 5.5a.2 Deliver the first production-composed CL Timeout vertical for
      active authenticated terminal disconnect with one blocked cleanup owner.
      Use the production ten-second/maximum-thirty-second TimeProvider watchdog,
      advance a manual provider to exact equality, prove
      `host_cleanup_timeout`, release the lifecycle gate, reject replacement
      Start with `host_cleanup_unconfirmed`, retain the same real cleanup task,
      drain both nodes after releasing the owner, and keep restart latched. Move
      only CL Timeout from M to P and retain all stated limitations.
      Exact implementation
      `685225ed92b76ee2e6f4800b9c97f8baf2af378d` adds one coordinator unit row and
      the 42nd production-composed managed loopback tracer. Both freeze the
      active authenticated-disconnect path while the original host Connection
      `DisposeAsync` is blocked, prove no timeout at T-1 tick and exact
      `host_cleanup_timeout` at equality, then prove lifecycle-gate release and
      zero-authority replacement rejection with `host_cleanup_unconfirmed`.
      Releasing the owner completes the same real cleanup, drains the tracer's
      two authenticated protocol-1.7/FSM1 nodes, and leaves the restart latch
      sticky. Exact-tree local macOS Debug/Release builds report zero warnings
      and errors; solution, Desktop, and tracer suites pass `2584/2584`,
      `720/720`, and `42/42` in both configurations; format, composition,
      simulator, and direct/transitive vulnerability checks pass. Exact-SHA CI
      `33311180093` and CodeQL `33311180128` succeed. Downloaded artifacts prove
      `2584/2584` with every non-success counter zero on each hosted OS,
      Gitleaks 208/0, CodeQL 52/0 with zero exact-ref open alerts, and three
      verified reproducible unsigned packages. This moves only CL Timeout from
      M to P. This slice does not cover Stop/Dispose-first, timer faults, late
      failure/OOM, pre-generation cleanup, or the complete per-boundary reject/
      throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix. Tasks 5,
      5.5a, 5.5, every
      native/physical/signing/notarization/release gate, and the Goal remain
      open; `CreateProduction()` remains unavailable. Details are in the
      [bounded cleanup-confirmation evidence](../../../docs/evidence/2026-08-30-bounded-cleanup-confirmation.md).
      _Requirements: NR8.9-NR8.16, NR10_
    - [ ] 5.5a.3 Extend bounded confirmation across explicit Stop, Dispose,
      cleanup-wins/equality races, timer setup/disposal failure, late non-fatal
      failure, fatal OOM, concurrent terminators, every remaining owner, and
      pre-generation cleanup before claiming Task 5.5a complete. Freeze explicit
      Stop so throw, exact caller cancellation, or `FullyStopped == false` runs at
      most one no-caller-cancellation fallback while retaining that initial
      exception, cancellation, or unconfirmed result as the primary outcome;
      `FullyStopped == true` never repeats Stop.
      _Requirements: NR8.9-NR8.17, NR10_
      - [x] 5.5a.3a Deliver external Dispose-first bounded confirmation for a
        stable active generation with an uncontended lifecycle gate. Set the
        disposed gate, then close Admission and atomically publish
        `active -> retiring`, the one real cleanup task, confirmation operation,
        and watchdog before any blocking controller or owner call. Prove one
        blocked host Connection owner, T-1 pending state, exact-equality
        `host_cleanup_timeout`, the same public Task and exception instance for
        concurrent/later external Dispose, cleanup attach-only behavior after a
        later terminal callback's existing synchronous safety prefix, late true
        drain, and immutable public outcome. Callback-origin Dispose remains a
        non-waiting signal into that same operation; Start after explicit Dispose
        preserves `ObjectDisposedException` before authority.
        Exact implementation
        `ea984fb01cad46ab128c6d294835df59327aa8ac` adds one deterministic
        coordinator row for a stable active generation and uncontended lifecycle
        gate. External Dispose publishes closed admission, `active -> retiring`,
        one real cleanup task, one confirmation, and one timer before a blocked
        host Connection owner. A later authenticated-disconnect callback runs its
        existing synchronous safety prefix and attaches cleanup exactly once to
        that operation. T-1 remains pending; exact equality produces one stable
        `host_cleanup_timeout`; concurrent, later, and post-drain external
        Dispose calls share the same Task and exception instance. Start retains
        `ObjectDisposedException` precedence and all authority baselines; late
        owner drain clears `retiring` and the timer without changing the public
        result. Local Debug/Release passes the focused row `1/1`, twenty fresh
        processes `20/20`, coordinator `117/117`, Desktop `721/721`, and
        solution `2585/2585`; builds, format, composition, simulator, and
        dependency audit pass. Exact-SHA CI `33314229467` and CodeQL
        `33314229459` succeed. Each hosted OS passes `2585/2585` with every
        non-success counter zero, Gitleaks reports 208/0, CodeQL reports 52/0
        with zero exact-ref open alerts, and all three reproducible
        version-`0.1.222` unsigned packages pass. This adds no 43rd tracer and
        promotes no matrix cell. Stop-first, lifecycle-gate contention, timer
        faults, late cleanup fault/OOM, pre-generation cleanup, and other owners
        remain open, so Tasks 5, 5.5a.3, 5.5a, and 5.5 and the Goal remain open;
        `CreateProduction()` remains unavailable. Details are in the
        [Dispose-first bounded cleanup evidence](../../../docs/evidence/2026-08-30-dispose-first-bounded-cleanup.md).
        _Requirements: NR8.9-NR8.16, NR10_
      - [x] 5.5a.3b Deliver Stop-first bounded confirmation for one stable active
        generation with an uncontended lifecycle gate. Claim the generation,
        close Admission, and publish `active -> retiring`, one real cleanup
        task, one confirmation operation, and one watchdog before any
        potentially blocking controller Stop or owner call. The exact caller
        token applies only to the first controller Stop attempt. Freeze two
        separate deterministic scenarios: (1) cancel that token after
        publication and before the confirmation deadline, retain the exact
        cancellation as primary, invoke exactly one fallback with
        `CancellationToken.None`, complete the same real cleanup, expose the
        same cancellation from public Stop and later Dispose, and keep restart
        fail-closed; and (2) block the first Stop through T-1 and exact deadline
        equality, publish `host_cleanup_timeout`, then release a
        `FullyStopped == true` result and
        drain the same real task without a fallback or timeout mutation. Keep
        concurrent Stop/Dispose/callback precedence, ordinary throw,
        `FullyStopped == false` combinations, lifecycle-gate contention, timer
        faults, late cleanup fault/OOM, pre-generation cleanup, and every other
        blocked owner open. This slice closes none of Tasks 5, 5.5a.3, 5.5a, or
        5.5, any native/physical/signing/notarization/release gate, or the Goal,
        and it does not make `CreateProduction()` available.
        Exact implementation
        `681842290d44f9524eab33550b307bad76017fbc` adds two deterministic
        coordinator rows. Both publish closed Admission, `active -> retiring`,
        one real cleanup task, one confirmation, and one timer before the first
        controller Stop attempt. The caller-cancellation row proves the exact
        token reaches only that attempt, exactly one
        `CancellationToken.None` fallback continues cleanup, public Stop and
        later Dispose expose the same cancellation instance, and restart stays
        fail-closed. The blocked-Stop row proves T-1 pending state, exact-
        equality `host_cleanup_timeout`, a late `FullyStopped == true` drain
        without fallback, and immutable timeout identity. Local Debug/Release
        passes focused `2/2`, twenty fresh processes per configuration with
        `40/40` case executions, coordinator `119/119`, Desktop `723/723`, and
        solution `2587/2587`; builds, format, composition, simulator, and dependency
        audit pass. Exact-SHA CI `33317026854` and CodeQL `33317026837`
        succeed. Each hosted OS passes `2587/2587` with every non-success
        counter zero, Gitleaks reports 208/0, CodeQL reports 52/0 with zero
        exact-ref open alerts, and all three reproducible version-`0.1.224`
        unsigned packages pass. This adds no 43rd tracer and promotes no matrix
        cell. Concurrent initiator precedence, ordinary throw,
        `FullyStopped == false`, lifecycle-gate contention, cleanup-winner/
        equality races, timer faults, late cleanup failure/OOM, pre-generation
        cleanup, and other owners remain open. Tasks 5, 5.5a.3, 5.5a, and 5.5
        and the Goal remain open; `CreateProduction()` remains unavailable.
        Details are in the
        [Stop-first bounded cleanup evidence](../../../docs/evidence/2026-08-30-stop-first-bounded-cleanup.md).
        _Requirements: NR8.9-NR8.16, NR10_
      - [x] 5.5a.3c Deliver the narrow external Dispose-first late-failure ledger
        slice for one stable active generation and an uncontended lifecycle gate.
        Inject non-fatal owner failure A at formal Emergency Stop registration
        disposal, block the later authenticated host Connection disposal through
        T-1 pending state and exact-equality cleanup-confirmation timeout, then
        release it to produce non-fatal owner failure B. Freeze the terminal
        ledger as exactly the flat sequence
        `[stable timeout, owner A, owner B]`: retain the timeout instance exposed
        by the shared immutable public Dispose task, retain the exact A and B
        instances, leave no nested `AggregateException`, and append the real
        cleanup result once. Prove concurrent, later, and post-drain external
        Dispose calls share the same Task and timeout, every tracked owner and
        budget drains, the timer drains, and `retiring` clears after late
        settlement. Limit the production change to recursively flattening
        non-fatal aggregates during terminal-ledger append. Do not claim OOM,
        ordinary Stop throw or `FullyStopped == false`, timer faults,
        cleanup-completion wins, lifecycle-gate contention, pre-generation
        cleanup, another initiator or owner combination, or a
        production-composed tracer. Keep Tasks 5, 5.5a.3, 5.5a, and 5.5, every
        native/physical/signing/notarization/release gate, and the Goal open;
        keep `CreateProduction()` unavailable and promote no matrix cell.
        Exact implementation
        `4daf82ce2eaeaba582eaf541fdf643daa4f7b73b` recursively flattens
        non-fatal terminal-ledger aggregates and adds one deterministic
        coordinator row. The old implementation exposed direct children
        `[timeout, Aggregate(A, B)]`; a breadth-first/flatten mutation exposed
        `[timeout, B, A]`. The final deep-A/direct-B fixture proves exact flat
        `[timeout, A, B]` order and identity, one real-cleanup append, immutable
        shared public Dispose Task and timeout, T-1 pending state, exact-equality
        timeout, and complete late owner, budget, timer, and retiring drain.
        Strict reviews report zero P0/P1/P2. Local Debug/Release passes focused
        `1/1`, twenty fresh processes per configuration with `20/20`
        executions, coordinator `120/120`, Desktop `724/724`, and solution
        `2588/2588`; builds, format, diff, composition, simulator, and dependency
        audit pass. Exact-SHA CI `33318946768` and CodeQL `33318946770`
        succeed. Each hosted OS passes `2588/2588` with every non-success
        counter zero, Gitleaks reports 208/0, CodeQL reports 52/0 with zero
        exact-ref open alerts, and all three reproducible version-`0.1.226`
        unsigned packages pass. This closes only Task 5.5a.3c, adds no 43rd
        tracer, and changes no matrix status. Fatal OOM, other late-failure
        combinations, ordinary Stop throw, `FullyStopped == false`, timer
        faults, cleanup-winner/equality races, lifecycle-gate contention,
        pre-generation cleanup, other initiators and owners, and the complete
        deterministic failure ledger remain open. Tasks 5, 5.5a.3, 5.5a, and
        5.5 and the Goal remain open; `CreateProduction()` remains unavailable.
        Details are in the
        [late cleanup-failure ledger evidence](../../../docs/evidence/2026-08-30-late-cleanup-failure-ledger.md).
        _Requirements: NR8.9-NR8.16, NR10.8_
      - [ ] 5.5a.3d Preserve first-fatal OOM dominance across late watchdog-
        release and owner-cleanup failures. Use one stable active generation,
        external Dispose-first initiation, an uncontended lifecycle gate, and a
        healthy manual watchdog that is physically released before its disposal
        hook throws a deeply nested OOM A. Block the later authenticated host
        Connection owner through T-1 and exact-equality timeout, then release it
        to throw direct OOM B. Prove the shared public Dispose Task and exact
        `host_cleanup_timeout` remain immutable, the terminal diagnostic keeps
        the original A instance rather than B or an aggregate, both failures are
        actually attempted once, every independently safe owner and budget
        drains, the timer count returns to zero, and `retiring` clears. Make the
        earliest fatal committed under the terminal-failure gate permanently
        dominant through both the normal recorder and its allocation-failure
        fallback, without changing non-fatal flattening, cleanup order,
        confirmation winner, Stop behavior, or public completion. Do not claim
        non-fatal timer release, timer creation/arm/callback faults, another
        initiator or owner combination, lifecycle-gate contention,
        cleanup-completion wins, pre-generation cleanup, or a production-
        composed tracer. Keep Tasks 5, 5.5a.3, 5.5a, and 5.5, every native/
        physical/signing/notarization/release gate, and the Goal open; keep
        `CreateProduction()` unavailable and promote no matrix cell.
        _Requirements: NR8.13-NR8.16, NR10.8_
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
