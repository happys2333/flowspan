# Flowspan v1 Implementation Plan

Status legend: `[ ]` pending, `[-]` in progress, `[x]` complete. A checked task
means its linked evidence exists; it does not imply the entire product works.

## 0. Product and engineering baseline

- [x] 0.1 Record the approved v1 scope as EARS-style acceptance criteria.
  - Covers desktop scope, Activity semantics, safety, local-first behavior, and
    evidence honesty.
  - _Requirements: R1–R12_
- [x] 0.2 Define normative domain language and ambiguity rules.
  - _Requirements: R3–R8, R11_
- [x] 0.3 Record initial architecture and single-language technology choice.
  - _Requirements: R1, R8, R12_
- [x] 0.4 Create threat model, test strategy, clean-room policy, and v1 release
  checklist.
  - _Requirements: R2, R9, R12_

## 1. Reproducible headless workspace

- [x] 1.1 Pin the .NET SDK and create solution-wide deterministic, nullable,
  analyzer, warning, formatting, and package-lock settings.
  - _Requirements: R1, R12_
- [x] 1.2 Add domain, protocol, application, diagnostics, simulator, and test
  projects with enforced dependency direction.
  - _Requirements: R12.1_
- [x] 1.3 Add developer commands and CI jobs for Windows, macOS, and Linux.
  - Third-party Actions are pinned to reviewed immutable commits and use the
    Node 24 action runtime; release tags and query evidence are recorded in the
    dependency inventory. The resulting Windows/macOS/Linux CI, gitleaks, and
    CodeQL v4 runs pass at `6bf191b`.
  - _Requirements: R1.3, R12.4–R12.5_

## 2. Two-node semantic handoff slice

- [x] 2.1 Implement validated IDs, Activity descriptor metadata, sensitivity,
  revision, placement, capability, and receipt value types.
  - _Requirements: R3.1–R3.2, R9.2, R11.1_
- [x] 2.2 Implement in-memory node, adapter, journal, fake clock, and deterministic
  transport ports.
  - _Requirements: R4, R8.4, R12.3_
- [x] 2.3 Implement semantic handoff and target validation with a portable note
  adapter.
  - _Requirements: R3.1–R3.4, R4.1, R4.5_
- [x] 2.4 Ship a CLI simulator scenario and unit/integration tests proving two
  nodes can hand off while the source remains active.
  - _Requirements: R3, R4.1, R12.3_

## 3. Reliable operations and protocol

- [x] 3.1 Implement version envelope, bounded codec, negotiation, golden fixtures,
  and compatibility errors.
  - _Requirements: R8.3, R9.1, R12.3_
- [-] 3.2 Implement move/replace ordering, undo capsule metadata, and idempotent
  operation journal.
  - _Requirements: R4, R11.2_
- [-] 3.3 Implement atomic swap prepare/reserve/decision/commit/recovery state
  machines.
  - _Requirements: R5_
- [-] 3.4 Add generated transition/property cases and deterministic drop,
  duplicate, delay, disconnect, and journal-failure tests.
  - _Requirements: R4.5, R5, R8.4, R12.3_

## 4. Device security

- [-] 4.1 Freeze identity, pairing transcript, SAS, ECDH/HKDF/AEAD, nonce, and key
  rotation formats in a reviewed ADR with test vectors.
  - Identity, pairing, authenticated-handshake, KDF, and encrypted-frame v1
    formats are recorded and exercised. The canonical pairing hello now has a
    committed golden hash; key rotation, additional cryptographic golden vectors,
    and independent review remain open.
  - _Requirements: R1.1–R1.2, R2, R9.1_
- [-] 4.2 Implement identity/secret-store ports, pairing state machine, trust
  records, capability grants, revocation, and identity-change handling.
  - The identity-store port, atomic load-or-create lifecycle, deletion, and
    explicitly degraded in-memory store are implemented. The versioned 1 KiB
    identity payload codec rejects hostile and non-canonical input. ADR 0006,
    `ITrustStore`, the 64-peer/64 KiB canonical trust codec, and an atomic
    payload-backed persistent repository cover restart, corrupt input,
    cancellation, concurrent mutation, and failed-save rollback. macOS uses
    atomic `SecItemUpdate`; Windows uses domain-separated CurrentUser DPAPI and
    an fsynced same-directory atomic replace. Linux uses a separately attributed
    Secret Service item, bounded per-invocation Base64 output, stdin-only secret
    transport, and a cross-process replacement lock. A live desktop Secret
    Service round trip remains open. Peer revocation and capability
    downgrade atomically remove session eligibility, stop every affected
    registered session before returning, reject concurrent new sessions, and
    surface aggregate stop failures without skipping another session. Awaited
    coordinator shutdown also blocks admission and stops all remaining
    registered sessions. A bounded `FSP1` ceremony now composes direct TCP,
    version negotiation, transcript proof before prompting, matching-SAS decision
    ports, dual signed confirmation, distinct completion proofs, local capability
    grants, a whole-ceremony deadline, and Trust registration. Reject, timeout,
    tamper, proven identity conflict, same-key re-pairing, persistence failure,
    cancellation, and cleanup faults have deterministic tests; one same-process
    real loopback test covers both endpoints. The 22-project gate and 268 tests
    pass locally and on Windows, macOS, and Ubuntu hosted runners for commit
    `79dae12`; secret scan and CodeQL also pass for that commit. One bounded
    production listener now classifies only `FSP1`/hello or `FSH1`/hello,
    transfers the pre-read frame to one decoder, separates pairing/session/total
    capacity, and serializes pairing Trust registration with revoke and session
    admission through one coordinator. Same-port loopback tests pair, close, and
    reauthenticate a new connection; pending prompts, hostile selectors,
    capacity, injected selection timeout, cancellation, and fatal accept drain
    are covered. The 22-project gate and 287 tests pass locally and on Windows,
    macOS, and Ubuntu hosted runners at `8e2a410`; secret scan and CodeQL pass for
    the same commit. Desktop confirmation UI and physical two-person SAS evidence
    remain open.
  - _Requirements: R2, R9.2, R9.6_
- [-] 4.3 Implement encrypted framed sessions and negative tests for tamper,
  replay, downgrade, expiry, and key substitution.
  - Directional AEAD frames and a four-message authenticated ephemeral TCP
    handshake are implemented with tamper, replay, downgrade, expiry, key
    substitution, and hostile-length tests; explicit Finished/rekey and broader
    hostile-peer testing remain open.
  - _Requirements: R8.3, R9.1_
- [-] 4.4 Implement Windows Credential Manager/DPAPI, macOS Keychain, and Linux
  Secret Service adapters with marked degraded-mode behavior.
  - ADR 0005 selects byte-oriented platform mechanisms and rejects silent
    plaintext fallback. The shared bounded payload bridge and Windows
    CurrentUser-DPAPI/atomic-file adapter are implemented and pass hosted
    Windows native smoke. The macOS Security.framework adapter passes local
    and hosted native/contract tests. The Linux secret-tool adapter has bounded
    process and fake contracts; Ubuntu hosted CI verifies missing-tool recovery,
    bounded output, cancellation, and process-tree termination. A live,
    unlocked desktop Secret Service round trip remains open. macOS additionally
    persists the bounded trust snapshot with update/add race recovery and passes
    local native create/restart/update/revoke evidence. Windows adds a separate
    DPAPI trust context and atomic protected-file replace with cross-platform
    contracts and passes hosted native create/restart/update/revoke plus domain
    separation. Linux identity/trust payload adapters and bounded process
    contracts are implemented; Ubuntu CI accepts trust output within its larger
    bounded envelope and rejects over-limit output, while live unlocked desktop
    Secret Service evidence remains open.
  - _Requirements: R9.6_

## 5. LAN discovery and reconnection

- [x] 5.1 Spike DNS-SD implementation choices and record dependency/security
  decision.
  - Makaretu 0.27.0 is provisionally selected behind an isolated browser
    adapter after comparison with Windows DNSAPI, macOS `dns_sd`, and Linux
    Avahi lifecycles. Its age, transitive graph, physical reliability, and
    package-license provenance remain explicit release risks.
  - _Requirements: R2.1, R8.1, R12_
- [x] 5.2 Implement signed minimal discovery offers and direct framed TCP
  transport.
  - Signed, short-lived canonical offers, identity-change detection, expiry,
    replay rejection, deterministic in-memory discovery, direct TCP connection,
    bounded framing, and secure control upgrade are implemented. The bounded
    DNS-SD TXT codec, split-record cache, provisional production
    browser/publisher, timed signed-offer refresh, and current-trust candidate
    source are implemented. One bounded listener now authenticates different
    current trusted peers on the same port, rechecks the authenticated key before
    capability registration, isolates peer/handler failure, and drains revoked
    or shutdown sessions. The 22-project gate and 246 tests pass locally and on
    hosted Windows, macOS, and Ubuntu at `657c9dd`, together with secret scan and
    CodeQL. Physical multicast evidence remains task 5.4. These are contract
    results, not physical-network evidence.
  - _Requirements: R2.1, R8.1, R9.1_
- [-] 5.3 Implement bounded reconnect/backoff and reauthentication across network
  changes and restarts.
  - Bounded deterministic exponential backoff and a serialized peer-session
    supervisor are implemented. The supervisor distinguishes transient failure,
    authenticated-session end, and permanent rejection; network changes cancel
    and drain the old boundary operation, coalesce bursts, reset backoff, and
    start no overlapping attempt. A BCL network-address-change adapter and real
    cancellable delay are present, and the lifecycle contracts pass hosted
    Windows, macOS, and Ubuntu CI. A verified-candidate source boundary now
    composes current trust/capability checks, a fresh authenticated TCP handshake,
    post-handshake coordinator registration, active revoke/downgrade draining,
    and structured permanent stop reasons. The production DNS-SD browser now
    feeds the candidate source, publishes the current short-lived offer, and
    recreates its isolated mDNS stack on outer address-change events. Physical
    publication/discovery, sleep/wake, peer-restart, and interface-churn evidence
    remain open.
    The composed slice passes 207 tests plus secret scan and CodeQL on hosted
    Windows, macOS, and Ubuntu runners at `fc39d6e`.
  - _Requirements: R8.2_
- [ ] 5.4 Run physical two-device LAN tests and preserve evidence.
  - _Requirements: R2, R8, R12.5_

## 6. Mirror, driver, and protection

- [-] 6.1 Implement mirror lifecycle and monotonic expiring driver leases.
  - _Requirements: R6.1–R6.5_
- [-] 6.2 Implement platform protection-state, capture, input, and emergency-stop
  contracts plus deterministic fakes.
  - _Requirements: R9.3–R9.4_
- [ ] 6.3 Implement Windows capture/input/protected-surface adapters and native
  evidence.
  - _Requirements: R1, R6, R9_
- [ ] 6.4 Implement macOS capture/input/protected-surface adapters and native
  evidence.
  - _Requirements: R1, R6, R9_
- [ ] 6.5 Implement Wayland portal/PipeWire and explicit X11 fallback adapters
  plus native Linux evidence.
  - _Requirements: R1, R3.3, R6, R9_
- [ ] 6.6 Add bounded media/file channels, backpressure, and hostile-peer resource
  tests.
  - _Requirements: R3.3, R6, R8.4, R9_

## 7. Desktop experience

- [x] 7.1 Record Avalonia dependency/version decision and create the accessible
  desktop composition root.
  - Pin and audit the production and headless dependency family; record native
    platform limits and test-evidence boundaries in ADR 0007.
  - Implement protected local-identity startup, a truthful safety/empty state,
    visible focus, programmatic names, and keyboard-operable identity details.
  - Run the headless shell contract and degraded composition smoke on every CI
    OS without claiming native screen-reader or real-machine launch evidence.
  - Avalonia 12.1, protected identity startup, the headless shell contracts, and
    explicit TEST MODE validation pass with all 295 tests on hosted Windows,
    macOS, and Ubuntu at `3439e2d`; limits and run IDs are recorded in
    [desktop composition evidence](../../docs/evidence/2026-07-14-desktop-composition.md).
  - _Requirements: R10, R12_
- [-] 7.2 Implement unified device/Activity entry, progressive permission
  prompts, pairing, capability editing, and identity-change warnings.
  - 7.2a complete: bridge one verified inbound SAS request to a least-privilege
    desktop confirmation surface and prove accept/reject with a deterministic
    two-node ceremony; do not expose discovery or native permissions as
    connected yet.
    All 306 tests, secret scan, and CodeQL pass on hosted Windows, macOS, and
    Ubuntu at `592ebc0`; run IDs and limits are recorded in
    [desktop pairing evidence](../../docs/evidence/2026-07-14-desktop-pairing-confirmation.md).
  - 7.2b complete: immutable, stable Trust snapshots; fingerprint-conditional
    capability edit/revoke through `TrustSessionCoordinator`; authoritative
    refresh; protected-store production composition; async close; and the
    accessible persistent-device editor with two-step revoke all pass. All 330
    tests, secret scan, and CodeQL pass on hosted Windows, macOS, and Ubuntu at
    `f6bbbf0`; run IDs and limits are recorded in
    [desktop Trust evidence](../../docs/evidence/2026-07-14-desktop-trust-management.md).
  - 7.2c complete: explicitly enable one production listener plus minimized
    DNS-SD browse/advertise lifetime; list only clearly unverified candidates;
    bind a selected signed offer to the transcript-authenticated pairing
    identity before showing SAS; refresh Trust after an outgoing ceremony; and
    unwind partial startup or close safely.
    The implementation now also supervises listener and advertisement loops as
    one failure domain: an unexpected post-enable exit cancels its peer loop,
    withdraws discovery, releases the socket/browser, changes the UI to a
    sanitized retryable fault, and cannot leave an enabled-looking dead
    session. Candidate removal, canonical ordering, dynamic Trust
    reclassification, identity-substitution rejection without SAS, inbound and
    outbound Trust refresh, in-flight close, and background-fault retry have
    deterministic contract or loopback tests. All 371 tests, secret scan, and
    CodeQL pass on hosted Windows, macOS, and Ubuntu at `e95b1b8`; run IDs,
    commands, suite counts, and physical-evidence limits are recorded in
    [desktop local-pairing evidence](../../docs/evidence/2026-07-14-desktop-local-pairing-network.md).
  - 7.2d complete: compose trusted reconnect into the explicitly enabled
    desktop network lifetime; elect one Device-ID-ordered connector; project
    per-peer waiting/authenticating/authenticated-idle/retrying/blocked states;
    latch conflicting-fingerprint and authenticated identity-change warnings;
    and drain supervisors on Trust, network, or window lifecycle changes. This
    slice retains `NOT SHARING`, never auto-repairs Trust, and passes
    deterministic, same-host loopback, and headless UI tests. All 383 tests,
    secret scan, and CodeQL pass on hosted Windows, macOS, and Ubuntu at
    `0dd5a1a`; the original Ubuntu Headless dependency race, remediation, run
    IDs, commands, suite counts, and physical-evidence limits are recorded in
    [desktop trusted-reconnect evidence](../../docs/evidence/2026-07-14-desktop-trusted-reconnect.md).
  - 7.2e complete: require an acknowledged, platform-specific local-network
    permission preflight before the currently implemented explicit network
    lifetime. Review/cancel must open no socket; guidance must name minimized
    LAN-visible metadata, likely OS/firewall behavior, and revocation on
    Windows, macOS, and Linux. All 389 tests, secret scan, and CodeQL pass on
    hosted Windows, macOS, and Ubuntu at `c44a364`; commands, run IDs, suite
    counts, stress repetitions, and native-evidence limits are recorded in
    [desktop local-network permission evidence](../../docs/evidence/2026-07-14-desktop-local-network-permission-preflight.md).
  - Remaining for parent 7.2: feature-use screen-capture and
    accessibility/remote-input permission flows, aligned with platform tasks
    6.3–6.5, plus matching real-machine permission/accessibility evidence.
  - _Requirements: R1, R2, R8, R10_
- [-] 7.3 Implement operation preview, named degradation, persistent sharing
  indicator, recovery, receipt, and undo surfaces.
  - 7.3a complete: deliver one bounded `workspace.note/v1` semantic handoff
    from the desktop over an authenticated control session. Require an explicit
    source-preserving preview, name the unavailable Remote Window fallback,
    enforce source-side `activity.receive` plus target-side `activity.offer`,
    and show a payload-free receipt. The local 421-test candidate, negative and
    stress cases, simulator result, and evidence limits are recorded in
    [desktop Semantic Handoff evidence](../../docs/evidence/2026-07-15-desktop-semantic-handoff.md).
    All 421 tests, Secret Scan, and CodeQL pass on hosted Windows, macOS, and
    Ubuntu for implementation commit `c7cee09` and evidence commit `4d49bd9`;
    exact run/job evidence is linked above. This completion status is effective
    only after the closure commit itself passes the same final-HEAD workflows.
    Replace/swap, live-sharing indicators, recovery history, and compensating
    undo remain later slices.
  - 7.3b complete: expose bounded
    `workspace.note/v1` Move as a separate target-first preview and keyboard
    control. Close the source only after the target returns a verified committed
    receipt; preserve it after rejection, failure, or acknowledgement loss; and
    report `SourceCleanupFailed` as a committed duplicate warning. The desktop
    runtime rechecks source-side `activity.receive`, the target rechecks
    `activity.offer`, shared receipt automation is operation-neutral, and Move
    undo/recovery text does not reuse Handoff copy semantics. The local
    431-test candidate, three 20-process stress groups, simulator classification,
    package audit, exact-commit hosted matrix, Secret Scan, CodeQL, and evidence
    limits are recorded in
    [desktop Semantic Move evidence](../../docs/evidence/2026-07-15-desktop-semantic-move.md).
    All 431 tests, Secret Scan, and CodeQL pass on hosted Windows, macOS, and
    Ubuntu for implementation commit `eb83801` and evidence commit `3ea5e10`;
    exact run/job evidence is linked above. This completion status is effective
    only after the closure commit itself passes the same final-HEAD workflows.
  - 7.3c core/protocol in progress: implement a distinct bounded Replace for
    `workspace.note/v1`. The target requires `activity.replace`, an exact target
    ID/revision/descriptor digest, verified Adapter capture, and a stored
    15-minute target-owned undo capsule before incoming resume. Authenticated
    `activity.replace` request/result messages return only payload-free capsule
    metadata; exact retries, acknowledgement loss, revision/digest conflict,
    expiry, consumed capsules, and idempotent undo have deterministic tests plus
    a same-host encrypted loopback case. The historical 452-test evidence used
    process-memory state; the current candidate adds a single versioned durable
    snapshot for capsules, Replace/undo pending and terminal records, and
    consumption, protected by an OS-credential-backed AES-256-GCM atomic file.
    The protected durable-core sub-slice is complete at implementation commit
    `3652c9b`: a single bounded snapshot persists capsules, Replace/undo pending
    and terminal records, and consumption; reconstructed-process tests prevent
    duplicate destructive Adapter work; and OS-credential-backed authenticated
    files fail closed. All 472 tests, Secret Scan, and CodeQL pass on the exact
    implementation commit `3652c9b` and evidence commit `8137e57` across hosted
    Windows, macOS, and Ubuntu. Commands, run/job IDs, platform-API coverage,
    stress results, and explicit physical/native limits are recorded in
    [protected Replace recovery evidence](../../docs/evidence/2026-07-15-protected-replace-recovery-state.md).
    The query-only target-inventory sub-slice is complete at implementation
    commit `23d1ba4`: it filters to bounded payload-free same-kind targets,
    rechecks directional grants, binds strict authenticated query/results, and
    composes no destructive endpoint. All 516 tests, Secret Scan, and CodeQL
    pass on that exact implementation commit and evidence commit `4ed97e2`
    across hosted Windows, macOS, and Ubuntu. Commands, run/job IDs, stress
    results, and explicit physical/native limits are recorded in
    [Replace target inventory evidence](../../docs/evidence/2026-07-15-replace-target-inventory.md).
    The preview-only Desktop sub-slice is complete at implementation commit
    `4407de7`: it explicitly queries and refreshes the bounded inventory,
    compares one incoming Activity with one exact target snapshot, revokes
    confirmation on participant/snapshot changes, names stale and sanitized
    recovery states, and keeps production destructive Replace uncomposed. All
    533 tests, Secret Scan, and CodeQL pass on that exact implementation commit
    and evidence commit `d3727a9` across hosted Windows, macOS, and Ubuntu.
    Commands, run/job IDs, 20-process Desktop Replace stress, and explicit
    physical/native limits are recorded in
    [Desktop Replace preview evidence](../../docs/evidence/2026-07-15-desktop-replace-preview.md).
    The target-local receipt/recovery sub-slice is complete at implementation
    commit `04cad52`: it projects at most 64 payload-free protected Replace/undo
    records with unresolved-first ordering, known-field-only bindings, exact
    snapshot/expiry state, sanitized startup failure, and a keyboard-navigable
    read-only Desktop surface. All 541 tests, Secret Scan, and CodeQL pass on
    that exact implementation commit and evidence commit `2888dc3` across
    hosted Windows, macOS, and Ubuntu. Commands, run/job IDs, two 20-process
    stress groups, and explicit physical/native limits are recorded in
    [Desktop Replace receipt/recovery evidence](../../docs/evidence/2026-07-15-desktop-replace-recovery.md).
    The target-local visible-undo sub-slice is complete at implementation commit
    `e076bd2`: for `workspace.note/v1`, protected terminal history reconstructs
    only an unambiguous exact frontier; pending/ambiguous/orphan state fails
    closed; one private local endpoint enforces durable single-consume undo; and
    a keyboard-operable, snapshot-bound confirmation presents every recorded
    outcome without Activity content. All 568 tests, Secret Scan, and CodeQL pass
    on that exact implementation commit and evidence commit `b7269c5` across
    hosted Windows, macOS, and Ubuntu. Commands, run/job IDs, two 20-process
    stress groups, and explicit physical/native limits are recorded in
    [Desktop target-local Undo evidence](../../docs/evidence/2026-07-15-desktop-target-local-undo.md).
    The exact-confirmation destructive composition sub-slice is complete at
    implementation commit `30e2db4`: the protected endpoint is a Trust-bound
    production peer, the source revalidates fresh inventory before creating one
    command, target Replace and local undo serialize before journal creation,
    and the UI projects committed, rejected, recovery-blocked, and uncertain
    outcomes without removing the source. All 579 tests, two 20-process stress
    groups, Secret Scan, and CodeQL pass on that exact implementation commit
    across hosted Windows, macOS, and Ubuntu. Commands, run/job IDs, downloaded
    TRX counts, and explicit physical/native limits are recorded in
    [Desktop semantic Replace evidence](../../docs/evidence/2026-07-16-desktop-semantic-replace.md).
    Physical/native product evidence remains open.
    The earlier core/protocol evidence is recorded in
    [bounded Replace core evidence](../../docs/evidence/2026-07-15-bounded-replace-core.md).
    Consequently 7.3c and the parent task are not complete. See
    [ADR 0011](../../docs/adr/0011-bounded-replace-undo-capsule.md).
    - [x] 7.3c.1 Persist capsules, Replace pending/final receipts, undo
      pending/final results, and consumption markers in one bounded,
      authenticated target snapshot. Keep its random key in DPAPI, Keychain, or
      Secret Service; fail closed on key/store/tamper/version/bounds errors.
    - [x] 7.3c.2 Prove exact Replace and undo replay across reconstructed
      process state, pending recovery without duplicate Adapter work, atomic
      failure ordering, expiry cleanup, and supported-platform storage
      contracts. Do not mark 7.3c complete until the desktop flow is composed.
    - [x] 7.3c.3 Add a purpose-scoped authenticated Replace target inventory.
      Require source-side `activity.receive` and target-side
      `activity.replace`; disclose only bounded, payload-free, normal-sensitivity
      same-kind target snapshots; bind correlation/target/kind/deadline; and
      preserve stale ID/revision/digest rejection before destructive work.
    - [x] 7.3c.4 Add a keyboard-operable preview-only Desktop Replace flow.
      Query and refresh inventory explicitly; compare the incoming Activity with
      one exact target snapshot; revoke confirmation on every relevant change;
      name stale revision/digest refresh and sanitized failure recovery; and keep
      production `IReplacePeer` uncomposed until receipt/recovery and target-local
      undo surfaces exist.
      - _Requirements: R4.7-R4.10, R10.1-R10.4_
    - [x] 7.3c.5 Add a bounded, payload-free target-local Replace receipt and
      recovery surface. Load the protected state at Desktop startup; prioritize
      unresolved Replace/undo boundaries; show only known opaque IDs,
      participants, timestamps, redacted outcomes, and exact undo availability;
      mark incomplete pre-capture fields and truncation honestly; and keep other
      Activity work usable when this store fails closed. Do not add a recovery,
      undo, or destructive command in this slice.
      - _Requirements: R4.11, R10.3-R10.4, R11.1_
    - [x] 7.3c.6 Add target-local visible undo for an exact unexpired,
      unconsumed capsule. Bind one explicit keyboard-operable action to the
      selected recovery snapshot, present expiry and every terminal/pending
      outcome, and prove restart replay cannot repeat Adapter restoration.
      - _Requirements: R4.12, R10.3-R10.4, R11.2_
    - [x] 7.3c.7 Only after 7.3c.5-7.3c.6, compose production `IReplacePeer`
      and the source-side Desktop `ReplaceAsync` command. Revalidate the exact
      preview at send time, project the authenticated receipt/capsule reference,
      and retain truthful acknowledgement-loss and recovery behavior.
      - Exact implementation-commit hosted evidence and remaining limitations
        are linked above. This status becomes effective only after the commit
        carrying this evidence also passes the same final-HEAD workflows.
      - _Requirements: R4.4-R4.12, R9, R10, R11_
  - _Requirements: R3, R4, R5, R6, R10, R11_
- [ ] 7.4 Externalize user-visible strings and verify keyboard, screen reader,
  scaling, contrast, and reduced motion.
  - _Requirements: R10_

## 8. Groups, Scenes, and lifecycle

- [ ] 8.1 Implement ordered Activity Groups and versioned Scene plans without
  secrets.
  - _Requirements: R7.1–R7.2_
- [ ] 8.2 Implement deterministic apply, per-Activity result, replace protection,
  and compensating undo where safe.
  - _Requirements: R7.3–R7.4, R11.2_
- [ ] 8.3 Implement inspect/delete/export for trust, history, Scenes, and redacted
  diagnostics.
  - _Requirements: R9.5, R11_

## 9. Packaging and v1 acceptance

- [ ] 9.1 Add reproducible Windows, macOS, and Linux packaging, signing hooks,
  SBOM, license report, provenance, checksums, and update metadata.
  - _Requirements: R1, R12_
- [ ] 9.2 Execute the complete CI matrix and preserve named artifacts/results.
  - _Requirements: R1.3, R12.4–R12.5_
- [ ] 9.3 Execute real-machine install, permission, LAN, security, accessibility,
  failure, and lifecycle matrix on all three OS families.
  - _Requirements: R1–R12_
- [ ] 9.4 Close every item in `docs/release/v1-release-criteria.md`, document known
  limitations, and only then mark the long-term Goal complete.
  - _Requirements: R1–R12_
