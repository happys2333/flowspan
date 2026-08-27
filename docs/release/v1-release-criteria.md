# Flowspan v1 Release Criteria

Status: not met

No release may be called v1-complete until every mandatory item below is checked
with evidence. `Not applicable` needs an approved requirements change, not an
implementation convenience.

## Product behavior

- [ ] Semantic handoff works for every documented v1 descriptor kind. (R3, R4)
- [ ] Remote Window fallback is functional, visibly labelled, and permissioned
      on Windows, macOS, and Linux. (R3, R6, R9)
- [ ] Move preserves source state until target acknowledgement. (R4)
- [ ] Replace offers an honest undo capsule or blocks before destructive work.
      (R4, R11)
- [ ] Swap remains atomic across tested drops/restarts and surfaces recovery.
      (R5, R8)
- [ ] Mirror/view and driver transfer enforce lease ownership. (R6)
- [ ] Activity Groups and Scenes plan, apply, and report partial outcomes. (R7)
- [ ] Discovery, pairing, revoke, and reconnect work without Internet. (R2, R8)

## Security and privacy

- [ ] Cryptographic protocol and key storage have an accepted security ADR and
      independent maintainer review. (R2, R9)
- [ ] Every payload channel is authenticated and end-to-end encrypted. (R9)
- [ ] Capability matrix negative tests pass. (R2, R9)
- [ ] Sensitive/protected surface behavior fails closed on all platforms. (R9)
- [ ] Emergency stop is verified locally under peer/network/UI failure. (R9)
- [ ] Diagnostic canary/redaction tests and manual export inspection pass. (R9,
      R11)
- [ ] Dependency, secret, and static scans have no unmitigated high/critical
      finding. (R12)

## Reliability and compatibility

- [ ] Deterministic model/property suite covers all normative invariants. (R5,
      R6, R8, R12)
- [ ] Fault-injection suite covers every critical transaction I/O boundary.
      (R4, R5, R8)
- [ ] Protocol golden fixtures and supported-version negotiation pass. (R8)
- [ ] Sleep/wake, network change, peer restart, and disk/journal failure evidence
      exists. (R8, R11)
- [ ] Load/backpressure limits have measured results and safe failure behavior.
      (R8, R9)

## Desktop quality

- [ ] Feature permissions are requested progressively with correct rationale.
      (R1, R10)
- [ ] Every operation and degradation has a visible state, result, and recovery
      path. (R3, R10, R11)
- [ ] Keyboard, screen reader, scaling, contrast, and reduced-motion checks pass.
      (R10)
- [ ] User-visible strings are externalized; v1 language scope is documented.
      (R10)
- [ ] Trust, history, Scenes, and diagnostics can be inspected and deleted.
      (R11)

## Build and distribution evidence

- [ ] A clean checkout restores and passes format, Release build, and all tests
      using documented commands. (R12)
- [ ] Required CI checks are green on Windows, macOS, and Linux at the release
      commit. (R1, R12)
- [ ] Signed/notarized packages install, upgrade, launch, and uninstall on real
      supported machines. (R1, R12)
- [ ] SBOM, dependency licenses, checksums, provenance, and changelog are
      attached to the release. (R12)
- [ ] Crash recovery and local data migration are tested from the previous
      supported release candidate. (R8, R11)
- [ ] Known limitations explicitly separate simulator/CI evidence from untested
      real-machine behavior. (R12)

## Current evidence

- [2026-07-13 macOS headless foundation](../evidence/2026-07-13-macos-foundation.md):
  locked restore, format verification, warning-free Release build, 287 unit,
  integration, security, and platform-contract tests, simulator, and NuGet
  vulnerability query passed locally.
- [2026-07-13 macOS headless pairing ceremony](../evidence/2026-07-13-macos-pairing-ceremony.md):
  bounded canonical/hostile wire tests, transcript and completion proof,
  rejection/deadline/tamper/identity/persistence/cleanup negatives, and a full
  two-store loopback TCP ceremony passed. The confirmation UI, human SAS check,
  and physical-device evidence remain open.
- [2026-07-13 macOS unified TCP listener](../evidence/2026-07-13-macos-unified-listener.md):
  one port pairs and then authenticates a new connection; strict family
  selection, capacity isolation, coordinator-serialized Trust registration,
  deadline, cancellation, and fatal-accept contracts passed on loopback.
- [2026-07-13 macOS authenticated inbound listener](../evidence/2026-07-13-macos-inbound-listener.md):
  one real loopback port authenticated two different trusted peers; unknown-peer,
  key-substitution, capability, concurrency/backpressure, handler-failure,
  revoke, cancellation, and fatal-accept contracts passed locally. This is not
  physical-device or remotely reachable LAN evidence.
- [2026-07-13 macOS Keychain](../evidence/2026-07-13-macos-keychain.md):
  local Security.framework identity create/reload/delete, concurrent-add, and
  trust create/restart/update/revoke smoke passed with disposable Keychain items.
- [2026-07-13 hosted CI foundation](../evidence/2026-07-13-hosted-ci.md):
  locked restore, format, build, tests, simulator, secret scan, and CodeQL passed
  on committed slices through `8e2a410` using Windows, macOS, and Ubuntu hosted
  runners. The authenticated reconnect slice composes current trust/capability
  checks with a real loopback authenticated TCP session and revoke/downgrade
  draining. The CI maintenance slice verifies immutable Node 24 Action pins,
  the DNS-SD slices verify bounded browser/candidate/publisher contracts, and the
  shared inbound listener authenticates multiple current peers with bounded
  lifecycle/failure behavior. None is physical-LAN evidence.
- [2026-07-13 macOS DNS-SD browser/publisher](../evidence/2026-07-13-macos-dns-sd-browser.md):
  236 local tests cover the bounded TXT/candidate/publisher core and isolated
  provisional browser/publisher adapter, including dual-stack, split-record,
  cache-limit, refresh, withdraw, restart/replay, and injected failure contracts.
  No multicast socket or physical peer was used.
- [2026-07-14 desktop trusted-device management](../evidence/2026-07-14-desktop-trust-management.md):
  330 tests, warning-free builds, explicit TEST MODE composition, simulator,
  secret scan, and CodeQL passed on hosted Windows, macOS, and Ubuntu for the
  fingerprint-conditional persistent Trust editor. Physical credential-store,
  active-session, native accessibility, and LAN behavior remain open.
- [2026-07-14 desktop local pairing network](../evidence/2026-07-14-desktop-local-pairing-network.md):
  explicit network enable composes the production listener, minimized DNS-SD,
  transcript-bound outgoing pairing, authoritative Trust refresh, and bounded
  lifecycle recovery. Same-host and hosted evidence is not physical-LAN or
  two-person SAS evidence.
- [2026-07-14 desktop trusted reconnect](../evidence/2026-07-14-desktop-trusted-reconnect.md):
  383 tests, warning-free builds, explicit TEST MODE composition, simulator,
  secret scan, and CodeQL passed on hosted Windows, macOS, and Ubuntu after an
  Avalonia Headless-session race remediation. The production-composed
  same-host loopback proves one authenticated idle channel and truthful status,
  not physical-LAN, Activity, native-permission, or packaged-app behavior.
- [2026-07-14 desktop local-network permission preflight](../evidence/2026-07-14-desktop-local-network-permission-preflight.md):
  389 tests, warning-free builds, explicit TEST MODE composition, simulator,
  secret scan, and CodeQL passed on hosted Windows, macOS, and Ubuntu. The
  acknowledged platform-specific education boundary starts no networking on
  review/cancel; it is not native prompt, firewall, revocation, screen-capture,
  remote-input, or physical-LAN evidence.
- [2026-07-15 desktop Semantic Handoff](../evidence/2026-07-15-desktop-semantic-handoff.md):
  the local 421-test candidate covers one bounded `workspace.note/v1` encrypted
  Handoff, directional authorization, exact payload-free receipt binding,
  source-preserving preview, named degradation, truthful uncertain outcome, and
  lifecycle order. All 421 tests, Secret Scan, and CodeQL pass on hosted Windows,
  macOS, and Ubuntu for implementation commit `c7cee09` and evidence commit
  `4d49bd9`. Physical-device, native accessibility, and arbitrary-application
  evidence remain pending.
- [2026-07-15 bounded Replace core and authenticated protocol](../evidence/2026-07-15-bounded-replace-core.md):
  452 local tests and exact implementation-commit hosted Windows/macOS/Ubuntu
  CI, Secret Scan, and CodeQL pass for the descriptor-only `workspace.note/v1`
  core. Target snapshot binding, store-before-resume, payload-free capsule
  reference, retry, expiry, consume, undo, acknowledgement-loss, and encrypted
  same-host loopback contracts are covered. The capsule/undo journals remain in
  memory and desktop Replace remains deliberately uncomposed, so the Replace
  product criterion is still unchecked.
- [2026-07-15 protected Replace recovery state](../evidence/2026-07-15-protected-replace-recovery-state.md):
  472 local and hosted tests prove one bounded durable snapshot for capsules,
  Replace/undo pending and terminal records, and consumption; reconstructed
  process replay without duplicate Adapter work; authenticated AES-256-GCM
  atomic files; real macOS Keychain and hosted Windows DPAPI API smoke; and
  supported-platform storage contracts. Ubuntu uses a fake Secret Service
  runner, and the desktop Replace flow remains deliberately uncomposed, so the
  Replace product criterion is still unchecked.
- [2026-07-15 purpose-scoped Replace target inventory](../evidence/2026-07-15-replace-target-inventory.md):
  516 local and exact-implementation-commit hosted tests prove directional
  authorization, bounded same-kind payload-free target projection, strict
  query/result binding, global pending-correlation exclusion, acknowledgement
  loss, same-host encrypted loopback, and query-only Desktop composition.
  Destructive preview/confirmation/recovery/undo and physical/native product
  evidence remain open, so the Replace product criterion is still unchecked.
- [2026-07-15 Desktop Replace preview and confirmation](../evidence/2026-07-15-desktop-replace-preview.md):
  533 local and exact-implementation-commit hosted tests prove an explicit,
  keyboard-operable, payload-free preview that compares incoming and exact
  target snapshots, revokes confirmation on relevant changes, rejects stale
  async inventory, and keeps destructive production composition locked.
  Receipt/recovery presentation, target-local visible undo, destructive
  composition, and physical/native evidence remain open, so the Replace product
  criterion is still unchecked.
- [2026-07-15 Desktop Replace receipt and recovery](../evidence/2026-07-15-desktop-replace-recovery.md):
  541 local and exact-implementation-commit hosted tests prove a bounded,
  payload-free, unresolved-first projection of protected target-local Replace/
  undo state plus sanitized startup failure and a keyboard-navigable read-only
  Desktop panel. No recovery, undo, or destructive command exists; target-local
  visible undo, destructive composition, and physical/native evidence remain
  open, so the Replace product criterion is still unchecked.
- [2026-07-15 Desktop target-local Replace Undo](../evidence/2026-07-15-desktop-target-local-undo.md):
  568 local and exact-implementation-commit hosted tests prove bounded
  semantic-note restart reduction, exact live capsule eligibility, explicit
  keyboard-operable confirmation, protected pending/terminal outcomes, precise
  expiry/consume/stale reasons, and service-level fail-closed enforcement.
  Destructive source/target composition, physical/native recovery, and native
  accessibility remain open, so the Replace product criterion is still
  unchecked.
- [2026-07-16 Desktop exact-confirmation semantic Replace](../evidence/2026-07-16-desktop-semantic-replace.md):
  579 local and exact-implementation-commit hosted tests prove production
  Trust-bound target composition, source-side exact send-time revalidation,
  protected target serialization before journal creation, encrypted-loopback
  commit and capsule binding, truthful acknowledgement loss, source
  preservation, keyboard activation, and unchanged `NOT SHARING`. Secret Scan
  and CodeQL pass on the same implementation SHA. Physical two-device LAN,
  representative native protected-store/application recovery, native
  accessibility, and crash/power-loss evidence remain open, so the Replace
  product criterion is still unchecked.
- [2026-08-10 Desktop Remote Window workflow](../evidence/2026-08-10-desktop-remote-window-workflow.md):
  local and exact-commit hosted tests cover purpose-scoped target selection,
  preview-role refiltering, fail-closed selection clearing, Mirror-only
  production profile loopback, post-Trust-mutation refresh, fallback keyboard
  operation, and portable lifecycle/concurrency boundaries. Each hosted OS
  passed all 1542 tests, and Secret Scan, CodeQL, parsed artifacts, and
  reproducible unsigned packages also passed for the exact final commit. Every
  native/physical release gate remains open; no release criterion is closed by
  this portable/headless record.
- [2026-08-28 Native Remote Window Transport candidate](../evidence/2026-08-28-native-remote-window-transport-candidate.md):
  implementation `a75afb142c335d8da71e511c29e51b14ad2b3cf7` composes the
  production-listener `FSM1` path and proves frame-count/plaintext exhaustion in
  both media directions closes attachment and authenticated control before any
  over-limit wire frame, rejects the consumed route, and recovers through a fresh
  authenticated media session and route. Exact-tree local macOS evidence passes
  460 Transport and 131 Security tests in both Debug and Release plus all 1,878
  Release tests with zero warnings. Exact-commit CI `33109385771` passes all 1,878
  tests on Windows, macOS, and Linux plus Secret Scan and the three reproducible
  unsigned package jobs; CodeQL `33109385769` also passes. Desktop/native
  composition, physical Devices, accessibility, interactive quality, and signed
  packages remain open; this candidate closes no release criterion.
- [2026-08-20 Desktop quality and string externalization](../evidence/2026-08-20-desktop-quality-string-externalization.md):
  five neutral-English catalogs, the shared Desktop resource facade, XAML/C#
  presentation regression gates, culture-aware display formatting, and the
  existing deterministic keyboard, automation, sizing, contrast, and no-motion
  contracts pass locally and on exact-commit hosted Windows, macOS, and Ubuntu.
  Downloaded TRX, Gitleaks, and three-RID reproducible unsigned package evidence
  was independently parsed and verified. Native screen-reader, visible-focus,
  operating-system high-contrast, font/text-scaling, reduced-motion, signed
  package, and physical-machine gates remain open, so the related release
  criteria remain unchecked.

This is portable, headless Desktop, and hosted unsigned-package evidence only.
It does not satisfy physical-device, native permission/hardware, signed or
notarized real-machine package lifecycle, independent security-review, or full
product acceptance gates.
