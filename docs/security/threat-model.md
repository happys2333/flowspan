# Flowspan v1 Threat Model

Status: living document; security implementation is not yet release-ready

Method: assets/trust boundaries plus STRIDE-oriented abuse analysis

## 1. Security objectives

Flowspan must prevent an untrusted or revoked peer from learning Activity
content, viewing a window, or injecting input. A paired peer receives only
explicit capabilities. Users can always see and locally stop active sharing.
Loss of connectivity, stale state, or adapter uncertainty must remove authority
rather than grant it.

## 2. Assets

- long-lived device identity private key and trust records;
- Activity descriptors, file content, window frames, clipboard-like context;
- keyboard, pointer, and other input authority;
- secure-input/protected-surface state;
- Scene definitions, operation journal, receipts, and diagnostics;
- peer names and network metadata that reveal device presence.

## 3. Trust boundaries

1. Unauthenticated LAN discovery traffic -> pairing candidate channel.
2. Candidate peer -> user-confirmed trusted identity.
3. Authenticated peer -> per-capability authorization.
4. Network bytes -> bounded protocol decoder.
5. Flowspan core -> native capture/input/credential APIs.
6. Normal window -> sensitive or protected surface.
7. Structured events -> logs, crash reports, or exported diagnostics.
8. Application process -> future relay, codec, or third-party dependency.

## 4. Adversaries and assumptions

- An attacker may join the LAN, spoof discovery packets, scan ports, replay
  messages, interrupt traffic, or race a pairing attempt.
- A previously trusted device may be stolen, compromised, or revoked.
- A peer may be honest but buggy and send malformed, oversized, duplicated,
  delayed, or contradictory messages.
- A local unprivileged process may inspect ordinary files and logs.
- The operating system, its credential store, and the local user account are
  trusted for v1. Defending against a fully compromised kernel is out of scope.
- The user may misread or accidentally approve a prompt; pairings and sharing
  indicators must therefore be specific and revocable.

## 5. Threats and required mitigations

| ID | Threat | Required mitigations | Verification |
| --- | --- | --- | --- |
| T01 | Spoofed or malformed discovery offer | Signed short-lived canonical offer, fresh nonce refresh, prompt withdraw, minimal metadata, current trusted-key binding, bounded TXT/record/address caches, reject self/loopback/multicast endpoints, never trust discovery alone | forged/expired/random TXT, canonical publication profile, refresh/withdraw, identity-change, batch-limit, and candidate-address tests |
| T02 | Pairing MITM | Bounded canonical `FSP1` messages; fresh nonces and role/version-bound transcript; verify both identity signatures before prompting; matching SAS on both endpoints; explicit signed dual confirmation followed by distinct signed completion proofs; local-only capability choice; desktop code-comparison acknowledgement with zero-capability default and one active prompt; whole-ceremony timeout; verify a claimed identity-change key before warning | golden/hostile codec, altered transcript/signature/confirmation/completion, reject, prompt/network timeout, identity-conflict, two-node desktop-decision loopback, cancellation/stale-prompt, and headless keyboard tests |
| T03 | Peer impersonation after pairing | Bind signed candidates to the current trusted key; treat an inbound hello's claimed Device ID only as unauthenticated trust-record routing input; authenticate every secure-session transcript against the selected trusted key; for protocol 1.2 verify bidirectional encrypted Finished role/transcript/session bindings before control upgrade; reload trust and compare the authenticated key again before session registration; block identity-key changes; require Device ID plus current fingerprint for desktop trust mutation so stale UI cannot affect a replacement identity; permanent rejection outranks network-change retry | claimed-ID/key-substitution, Finished binding/tamper/omission, candidate-binding, stale-admin-snapshot, multi-peer loopback, and reconnect/registration-race integration tests |
| T04 | Replay/duplicate command or mismatched receipt | Session epoch, message/operation IDs, one atomic pending correlation reservation across Activity message types, bounded TTL, request/payload/descriptor digests, durable idempotency journal, and payload-free receipts/results bound to authenticated participants, purpose, Operation, Activity, and descriptor; protocol-1.7 Remote Window Preparation additionally repeats every exact binding under a domain-separated canonical SHA-256 digest and retains one terminal tombstone through its deadline or connection close | property/fault tests, digest tamper, cross-type correlation collision, unsolicited/wrong-correlation/wrong-Activity result tests, and Prepare/Ready replay/cross-request/late-terminal tests |
| T05 | Capability escalation | Deny by default; typed independent capabilities with documented peer-relative direction; read immutable trust projections; apply the complete edited grant only through the coordinator; explicit all-of/any-of session admission checked before connect and again after authentication; exact operation direction checked immediately before metadata/payload disclosure or use; purpose-scoped target inventories never substitute `activity.receive` for Mirror grants; for Remote Window Preparation only the source host checks its grant to the participant, while the receiving participant checks authenticated Trust and local policy without requiring or reinterpreting an opposite-direction reciprocal Mirror grant; revoke/downgrade persists first and drains or rejects every active handler whose requirement is no longer satisfied; retain failed peer-disconnect cleanup for retry without restoring revoked authority | permission matrix, any-of admission/final-alternative removal, immutable-snapshot, conditional-mutation, peer-disconnect retry, stop-failure, connection-registration race, purpose-scoped Remote Window target, complementary one-way Prepare grants, and outbound/inbound Activity authorization and inventory-revocation tests |
| T06 | Remote input or capture after authority/permission loss | Monotonic driver lease epochs, short expiry, local enforcement, emergency-stop epoch bump; permission revocation and start admission share one synchronized fact so revocation either rejects the crossing or stops the admitted start; Remote Window Prepare, Ready, and media attachment grant no capture, participant, Driver, input, or render authority, frame admission remains closed through host revalidation/Start/AddParticipant, and only exact final Admission state establishes the participant binding; failed, throwing, or cancelled capture admission attempts synchronously invoke local capture cleanup even when an adapter ignores cancellation and returns success, and never claim `Stopped` without confirmation; generation and inactive-boundary provenance stop an orphaned late successful Start without stopping a replacement session | lease model/property tests plus deterministic permission/start admission, Ready-before-capture and final-state-before-frame tests, cancellation-ignoring success, inactive-before-success, replacement-session isolation, and failed-start cleanup races |
| T07 | Sensitive content capture | Continuous protection-state probe, fail closed on unknown/stale, visible pause/blank state | platform contract + native manual tests |
| T08 | Malformed/oversized or cross-protocol input | classify only bounded `FSP1` pairing, `FSH1` authenticated-control, and distinct bounded `FSM1` media-attachment initial frames on the shared production listener; transfer every pre-read frame to exactly one decoder; treat the `FSM1` clear route locator only as lookup input, then require exact request/acknowledgement lengths, zero flags, clear/protected agreement, and AEAD-authenticated directed Device/route/Session/Activity/nonce bindings; enforce frame, depth, field, count, decompression, timeout, and allocation limits; decode protocol-1.2 Finished and protocol-1.3 `FSR1` KeyUpdate only after AEAD authentication; structurally validate one complete TopLeft still JPEG, with no concatenated/trailing image and bounded dimensions/pixels/decoded bytes, before pixel allocation | hostile shared-listener selector/capacity corpus; Finished and KeyUpdate wrong-kind/flag/length/trailing/tamper rejection; protocol-1.6 attachment fixtures and wrong-binding/tamper/truncation/trailing tests; JPEG format/animation/orientation/concatenation/dimension/pixel-bomb preallocation tests; protocol/state property tests |
| T09 | Descriptor opens dangerous target | schema validation, URL scheme allowlist, safe filename handling, no source paths, confirmation where needed | adapter security tests |
| T10 | Secret leakage in logs or history UI | structured allowlist logging and recovery projection, redaction before sinks, no payloads/raw input/private keys/request digests/exception text, bounded target-local display | redaction canary and recovery-snapshot projection tests |
| T11 | Journal tampering/rollback | restricted storage, authenticated records or database integrity, monotonic revisions, recovery conflict state, fail-closed startup presentation without blocking unrelated Activity work | persistence tamper and Desktop startup-fault tests |
| T12 | Compromised dependency/update | lock files, dependency review, hashes/signatures, SBOM, signed artifacts and update metadata | CI supply-chain checks |
| T13 | Resource exhaustion | per-peer rate, concurrency and size limits; at most 128 total inbound connection slots, with independent bounded pairing, authenticated-session, and media-attachment capacity; pairing messages limited to 4096 bytes and the whole pairing ceremony limited to two minutes by default/ten minutes maximum; the process-scoped in-memory operation journal binds at most 4,096 IDs and rejects an unknown ID before handler work when full while preserving known retries; Replace inventory limited to one strictly ordered page of 64 snapshots; Remote Window media routes are process-local, single-use, default to 32 with a 128 hard cap, expire after 30 seconds by default/two minutes maximum, independently retain at most 512 initiator-nonce fingerprints and 512 consumed route IDs for the maximum TTL, keep a live attached route's history slot until cleanup, and fail new admission closed rather than evict security-relevant history; attachment timeout defaults to two seconds with a ten-second maximum; JPEG input and decoded pixels are rejected against fixed byte/dimension/pixel bounds before large allocation; logical video is limited to 16 chunks/1 MiB, one active frame, one latest pending frame, one outstanding wire chunk, and bounded peer/session queues; non-cooperative async I/O retains borrowed encrypted buffers until the underlying operation finishes; stop/disposal initiation returns while a cancellation callback is blocked, while cleanup completion joins that callback and borrowed send before attempting later stages, aggregates throwing callbacks, and releases frame owners and budget before success or failure; bounded initial-family selection and handshake timeouts, queues, and DNS-SD record/instance/address caches; serialized reconnect and Desktop Remote Window start admission with bounded backoff/coalescing and one service-boundary crossing per inactive session; cancellation and backpressure; because media has no live rekey, terminate the attachment and owning control connection and perform a fresh authenticated handshake before either media direction exceeds `2^20` frames, 1 GiB plaintext, or its sequence/epoch boundary | selector/pairing/session/inventory/journal capacity and deadline tests; concurrent Remote Window start; media route dual-history capacity/TTL/non-reuse/timeout/race/cleanup tests; logical-frame chunk/assembly/latest-pending/budget-cleanup tests; non-cooperative read/write buffer-lifetime and throwing/blocking cancellation-callback tests; JPEG hostile preallocation and marker-grammar tests; hostile input, load/fault/DNS-batch, timeout, and reconnect-churn tests |
| T14 | Invisible monitoring | foreground sharing indicator on every participant, no unattended defaults, audit receipt, immediate local stop | UI accessibility/e2e tests |
| T15 | Downgrade to unsafe fallback | authenticated feature negotiation; signed highest-common-version offers; require encrypted Finished for mutually supported protocol 1.2+ and bounded live control rekey for 1.3+; require protocol 1.6+ for `FSM1` Remote Window media attachment, while 1.5 remains explicitly control/encrypted-frame compatible but cannot publish or claim a route; require protocol 1.7+ for host-selected Prepare/Ready and never approximate it with Activity transfer, unsolicited state, clear media, or unprepared Admission; retain every 1.5/1.6 fixture and behavior; retain 1.2 as a reconnect-at-bound compatibility path and 1.0/1.1 as named legacy modes; explicit capability and confirmation | altered-version signatures, 1.2 Finished sequence proof, 1.3 threshold/crossed-request rekey tests, protocol-1.6 feature-gate/fixture and protocol-1.5 route-rejection tests, protocol-1.7 Prepare/Ready fixtures and 1.6 rejection, compatibility presentation, and downgrade tests |
| T16 | Future relay reads content | application-layer E2E encryption independent of byte-forwarding relay | relay-as-attacker integration test |
| T17 | Hidden or premature platform/network privilege | request no capture/input/network privilege at launch; require feature-scoped rationale, exposed-data disclosure, prompt expectation, revocation path, and affirmative acknowledgement before the privileged boundary; cancel remains side-effect free | platform-guide, command-gating, no-start review/cancel, disable-reset, and Headless keyboard tests plus native grant/deny/revoke evidence |
| T18 | Destructive Replace without recoverable target state, or forged/stale target/undo metadata | purpose-scoped authenticated target inventory; distinct `activity.replace` capability and destructive message; exact target ID/revision/digest revalidation; Adapter capture plus application verification and target-owned store before resume; payload-free capsule reference; bounded expiry; exact-current replacement check; idempotent consume; bounded unresolved-first recovery snapshot; exact terminal-history reduction for the supported semantic tracer; global pending/`Recovering` fail-closed gate; private target-local endpoint plus service-level live eligibility preflight; destructive desktop activation blocked until target snapshot, confirmation, recovery, and undo are visible | inventory filtering/bounds/revocation, capture/store/revision/digest negatives, terminal-graph conflict/orphan/pending tests, recovery projection/truncation/startup-fault tests, direct-call preflight, retry/expiry/consume/stale reason tests, strict codec tamper tests, authenticated result binding, acknowledgement-loss and encrypted-loopback tests |

Task 5.4 commit `a75afb142c335d8da71e511c29e51b14ad2b3cf7` extends the
T08/T11/T13 managed Transport boundary through media budget exhaustion. Its
usage-limit type and accepting overloads are internal and permit only positive
limits no greater than the frozen production values, so test injection can shrink
but cannot raise or disable a production budget. A 2-by-2 same-host real-TCP
matrix uses frame limit 2 or plaintext limit 220 bytes in both directions. It
proves the last legal protected media operation, rejection before another wire
write, attachment and owning-control closure, empty route/session directories,
a fresh authenticated handshake with a different media session and route, and
rejection of the old `FSM1` request without consuming the new route. A separate
test rejects prior-session ciphertext without advancing fresh receive state and
then accepts fresh ciphertext. Media remains at epoch one; recovery never raises
a budget, rekeys in place, or republishes a consumed route.

The exact tree has local macOS Transport 460/460 and Security 131/131 results in
both Debug and Release, a full Release solution result of 1878/1878, and zero
warning-as-error build warnings. Exact-commit CI `33109385771` also passes
1878/1878 on Windows, macOS, and Linux, Secret Scan, and all three reproducible
unsigned package jobs; CodeQL `33109385769` passes with zero results. This remains
managed portable proof, not native/physical proof. Budget exhaustion combined
with an injected cleanup failure in one end-to-end path remains a P2 residual;
the two behaviors are covered separately. Bounded return still cannot be claimed
when a synchronous third-party `Stream.Dispose` itself blocks forever. Desktop
Task 5.5, native adapters, packaged accessibility, physical-device, and signed
release gates remain open.

### 5.1 Task 7.2c local-pairing evidence

| Threat | Implemented evidence boundary | Remaining evidence |
| --- | --- | --- |
| T01 | `DnsSdUnverifiedPairingCandidateSourceTests` cover malformed/expired/future-skewed/self/unsafe candidates, port-bound projection, removal, immutable canonical ordering, and authoritative Trust reclassification. `DnsSdPeerAdvertisementServiceTests` cover signed refresh and withdrawal. Discovery remains explicitly labelled unverified. | Physical multicast discovery and withdrawal on representative LANs; hosted matrix for the delivery commit. |
| T02 | `DiscoveryBoundPairingDecisionSourceTests` prove that SAS delegation occurs only after the transcript-authenticated Device ID and fingerprint match the pinned candidate and its signed offer verifies with that authenticated key within its lifetime. Mismatch and non-pairable Trust states reject before the desktop prompt. | Physical two-person, two-device SAS comparison and native accessibility observation. |
| T03 | `DesktopPairingIntegrationTests` substitute a different key under the advertised Device ID and prove the initiator sees no SAS while both Trust stores remain empty. Matching-key inbound and outbound ceremonies share the production runtime identity and Trust coordinator. | Broader trusted-reconnect identity-change outcome UI remains in task 7.2 after 7.2c. |
| T13 | `DesktopLocalPairingRuntimeTests` prove explicit enable, one serialized outbound pairing, cancellation/drain on close, partial-start cleanup, and injected post-enable advertisement failure that cancels the listener, withdraws, releases socket/browser resources, changes to a retryable fault, and starts a fresh session on retry. | Load/physical network churn evidence and the release-level resource gates remain open. |

These tests are deterministic contract and same-host loopback evidence. They do
not claim Windows, macOS, or Linux physical DNS-SD, firewall, or dual-machine
success. Hosted CI, secret scan, and CodeQL evidence must be attached to the
exact implementation commit before task 7.2c is complete.

### 5.2 Task 7.2d trusted-reconnect evidence plan

| Threat | Required implementation evidence | Evidence that remains physical |
| --- | --- | --- |
| T03 | A current-key signed offer is reconstructed only from protected Trust and verified before connect; a conflicting advertised fingerprint is excluded from candidate lookup and latched beside the expected fingerprint; a handshake-level identity change becomes a permanent warning without changing Trust. | Two physical devices changing/replacing an identity key, plus native notification and assistive-technology observation. |
| T05 | The elected outbound control channel and shared listener accept any current local `activity.offer`, `activity.receive`, `activity.replace`, `activity.swap`, `scene.apply`, `mirror.view`, or `mirror.drive`. Old all-of profiles remain strict, changing between any-of alternatives keeps the session, and removing the final eligible alternative drains it. Admission grants no operation: every Scene operation reloads current peer-relative `scene.apply`, every child independently rechecks its Activity authorization, and drive-without-view remains absent from every Mirror picker and input path. Complementary capability directions work under the deterministic smaller-Device-ID connector election, and an idle authenticated channel is never called sharing. | Physical active-session shutdown across sleep/wake and credential-store failure. |
| T13 | Device-ID ownership permits one active connector per pair; candidate changes wake waiting retries without overlapping attempts; disable, background-network fault, and close cancel and drain all workers and handlers. | Representative Wi-Fi/Ethernet interface churn, multicast loss, firewall prompts, and peer restart on Windows, macOS, and Linux. |

The implementation and hosted matrix evidence must be attached to the exact
7.2d delivery commit. Contract and loopback results do not prove physical DNS-SD
or cross-machine behavior.

### 5.3 Task 7.2e local-network permission-preflight evidence plan

| Threat | Required implementation evidence | Evidence that remains physical |
| --- | --- | --- |
| T01 | The preflight enumerates the signed fields visible on the LAN and explicitly excludes Activity content and Capability grants; its text is selected only from a closed Windows/macOS/Linux guide. | Packet capture on each supported OS proving the description matches the shipped advertisement. |
| T14 | Review and cancel do not call the network runtime; enable stays gated on explicit acknowledgement; the global `NOT SHARING` state remains unchanged after enable. | Native screen-reader and visible-focus observation in a packaged window. |
| T17 | Startup performs no privilege probe; each platform guide names likely prompt/firewall behavior and a revocation route; Disable clears acknowledgement so the next network lifetime requires a fresh review; fault retry remains visibly reviewed. | Real OS prompt grant, denial, settings revocation, firewall behavior, and recovery on Windows, macOS, and Linux. |

The contract deliberately does not claim that static guidance is a native
permission probe or that hosted runners changed firewall/privacy settings.
Screen-capture and remote-input preflights remain tied to their later feature-use
boundaries.

### 5.4 Task 7.3a portable-note Semantic Handoff evidence plan

| Threat | Required implementation evidence | Evidence that remains physical |
| --- | --- | --- |
| T04 | The strict Activity codec verifies request, payload, and descriptor digests and bounds the operation deadline to the encrypted envelope. A pending sender accepts only a receipt matching authenticated participants, protocol, correlation ID, Operation ID/kind, Activity ID/kind, and descriptor digest; unsolicited, wrong-correlation, wrong-Activity, disconnect, and unsupported-message cases fault closed or become acknowledgement-lost. | Cross-machine packet-loss, suspend/resume, and duplicate-delivery recovery over representative LANs. |
| T05 | Source disclosure requires its local `activity.receive`; target adapter use requires its local `activity.offer`. Control-session admission is any-of without weakening each Operation check, and Capability downgrade/revoke still drains through the coordinator. | Revocation during a physical transfer on each supported OS and protected-store failure recovery. |
| T10 | The receipt body contains no descriptor payload; UI failures and receipt summaries expose only allowlisted IDs/outcome/reason/timestamp. Canary tests search serialized receipts and visible errors. | Crash-report/minidump and exported-diagnostic review in packaged builds. |
| T15 | The preview says `SEMANTIC HANDOFF — SOURCE STAYS OPEN`, names `REMOTE WINDOW NOT AVAILABLE IN THIS BUILD`, and never presents move, mirror, driver transfer, or arbitrary process migration as available. | Native screen-reader, scaling, contrast, and translated-string review in packaged builds. |

The encrypted tests use same-host loopback and deterministic ports. They prove
the production framing, authentication, authorization, lifecycle, and UI
contracts on one host; they do not prove physical LAN reachability, two-machine
application behavior, or native platform permission handling.

### 5.5 Task 7.3b acknowledged Semantic Move evidence plan

| Threat | Required implementation evidence | Evidence that remains physical |
| --- | --- | --- |
| T04 | Move accepts only the precisely bound target receipt already required by 7.3a. Target rejection, delivery failure, and acknowledgement loss leave the source active; deterministic duplicate/lost-ack retry returns the journaled result without a second target resume. | Cross-machine packet loss immediately before/after receipt, peer restart, and recovery after sleep on representative LANs. |
| T05 | The source rechecks local `activity.receive` before channel use and the target reloads local `activity.offer` before adapter use. A live authenticated target that rejects the current grant cannot trigger source cleanup. | Capability revocation racing a physical transfer and protected Trust-store failure on each supported OS. |
| T10 | Move receipts and visible failure/warning text remain payload-free; target acceptance, source cleanup, and duplicate risk are expressed without note content or exception details. | Packaged crash-report, minidump, and exported-diagnostic review. |
| T15 | Handoff and Move have separate previews and confirmation controls. Move names target-first ordering, acknowledgement loss, and `SourceCleanupFailed`; the shared receipt is not labelled as Handoff. | Native screen-reader speech, focus order, scaling, contrast, and translated-string review in packaged builds. |

Same-host encrypted loopback proves the production control framing and the
target-first contract on one machine. Hosted Windows/macOS/Linux runners add OS
build and portable-contract evidence only; neither class proves two physical
devices or native accessibility.

### 5.6 Task 7.3c bounded Replace core evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T04 | `activity.replace.inventory` and its result bind authenticated participants, correlation, target, incoming kind, query deadline, capture/send ordering, strict schema, and one bounded canonical snapshot page. Transfer, inventory, and Replace cannot share a pending correlation. Unsolicited/wrong-correlation results fault closed and disconnect becomes acknowledgement-lost. Destructive `activity.replace` retains its existing exact Operation/target/capsule binding and durable replay. The target desktop projects unresolved durable boundaries before terminal history without replaying Adapter work. | Hosted final-HEAD matrix, cross-machine packet loss/retry, and physical sleep/wake interruption. |
| T05 | The source checks its current `activity.receive` before inventory and again before destructive channel lookup. On every query and destructive request, the target reloads the requesting peer's current `activity.replace`; same-session downgrade is rejected before catalog projection or mutation. `activity.replace` may admit an idle control channel but authorizes no other operation. | Explicit target-side confirmation and revocation observation on physical supported-platform pairs. |
| T10 | Inventory exposes only Activity ID/revision/descriptor digest/kind/normal-sensitivity title/Placement slot; payload, payload digest, origin, sensitive/restricted/inactive/non-local/different-kind/unsupported Activities stay local. Full target state remains in the protected target store, and destructive results/receipts remain payload-free. | Packaged crash/minidump/export canary inspection and independent protected-store review. |
| T18 | Inventory is purpose-scoped preview data, not mutation authority: it is limited to active normal local same-kind targets with a Replace-capable Adapter. Desktop re-queries at send time and matches device/ID/revision/digest/kind/title/placement before constructing a command. For `workspace.note/v1`, startup admits only unambiguous terminal-history frontiers; pending/`Recovering`, orphaned capsules or committed receipts/undos, receipt/capsule mismatches, conflicting transitions, unsupported kinds, and non-exact live catalog state fail closed. The protected endpoint serves both inbound Replace and confirmed local undo; service and target-peer preflights prevent a globally blocked attempt from reaching Adapter work. Expired, consumed, and stale known capsules preserve their exact rejection reason without Adapter restore. | Native Adapter evidence beyond `workspace.note/v1`, independent security review, and physical crash/power-loss evidence. |

The current candidate adds a Trust-bound production target peer and source-side
Desktop `ReplaceAsync` to the durable protected-state, purpose-scoped inventory,
snapshot confirmation, recovery projection, semantic-note restart reduction,
and confirmed local undo. Deterministic tests cover send-time stale selection,
both directional Trust revocations, encrypted-loopback commit and capsule
binding, unresolved-target rejection, pending duplicate disable, truthful
acknowledgement loss, and keyboard activation while `NOT SHARING` remains
unchanged. Hosted final-HEAD, physical/native recovery, and native accessibility
claims remain pending; this candidate alone does not satisfy the v1 Replace
release criterion.

### 5.7 Task 3.3a durable atomic-Swap coordinator evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T04 | Persist one exact Operation/correlation/deadline and both Device/Activity/revision/digest/token bindings before Prepare. Persist one participant-bound Commit or reason-bearing Abort before delivery. Exact replay returns the same decision; different Operation content conflicts; undecided restart can only record Abort. | Authenticated wire schema, Trust/capability replay tests, physical cross-device packet loss, sleep/wake, and endpoint restart. |
| T10 | The coordinator record excludes title, descriptor payload, payload digest, and exception text. Canary tests inspect both canonical plaintext and protected files. | Packaged crash dump, diagnostic export, and recovery-UI inspection. |
| T11 | Strict version/bounds/order/digest decoding rejects tamper; AES-256-GCM authenticates a Swap-specific `FSSF` file; the independent random key is held under Swap-specific DPAPI, Keychain, or Secret Service identifiers. Failed saves do not publish candidate state in memory, permanently block that journal instance, and require reopen so an ambiguously persisted Commit cannot be overwritten by Abort. | Abrupt power-loss/filesystem testing, rollback detection, live Linux desktop Secret Service, and independent storage review. |
| T18 | Abort-before-Prepare creates an idempotent participant tombstone, delayed Prepare cannot reopen it, and a Prepared Activity excludes an overlapping Swap. Any decision-delivery uncertainty remains `Recovering`; no single endpoint result is presented as atomic success. | Durable endpoint journal/reducer, explicit Desktop exact-selection confirmation, native Adapter eligibility, and user-visible recovery. |

The 3.3a evidence protects only the coordinator transaction. Task 3.3b below
adds the endpoint boundary; the Activity catalog, authenticated wire, product UI,
and physical restart evidence remain outside both slices.

### 5.8 Task 3.3b durable atomic-Swap endpoint evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T04 | Persist Prepared before acknowledgement and persist the exact Device/token-bound Commit or Abort before catalog mutation or acknowledgement. Reconstructed duplicate/reordered decisions are idempotent, Abort-before-Prepare remains a tombstone, and any overlapping Activity stays blocked until a recorded Commit has reached its exact replacement state. | Authenticated Swap wire schema, Trust/capability replay tests, sleep/wake, and physical cross-device packet loss. |
| T10 | Store the complete original and incoming Activity only in the private endpoint journal needed for restart reduction. Canary tests verify that protected files contain no plaintext and coordinator records, receipts, discovery, and diagnostics remain payload-free. | Packaged crash-dump, diagnostic-export, retention/deletion UI, and native filesystem inspection. |
| T11 | Strict version/bounds/order/enum/UTC/digest/participant decoding rejects hostile state. A purpose-separated `FSEF` AES-256-GCM file uses independent DPAPI, Keychain, or Secret Service identifiers; any save exception forces reopen before another write. | Abrupt power-loss/filesystem testing, rollback detection, live Linux desktop Secret Service, and independent storage review. |
| T18 | Commit reduction accepts only the exact original-to-replacement transition or the exact already-applied replacement. Any other catalog state remains `Recovering / RevisionConflict`; Prepared never guesses Abort after restart. | Persistent native Activity catalog protocol, explicit Desktop exact-selection and recovery UI, representative Adapter eligibility, and physical restart evidence. |

This slice protects endpoint recovery content but is not a general Activity or
application-process database. Hosted platform contracts and local macOS
Keychain evidence do not satisfy physical-device, abrupt-termination, or native
application recovery gates, so the v1 atomic-Swap release criterion stays open.

### 5.9 Task 3.3c authenticated atomic-Swap control evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T04 | Six strict Swap request/result schemas bind the authenticated sender, target, Operation, correlation, participant tokens, request/decision digests, exact descriptor revisions, and deadlines. One pending correlation spans every Activity operation type; unsolicited, expired-envelope, cross-operation, wrong-participant, and wrong-digest messages fault closed before endpoint work. Snapshot/Prepare send and response use their deadline and decision send/acknowledgement uses 30 seconds; a silent peer becomes acknowledgement loss and the session closes. | Hosted final-HEAD matrix, physical packet loss/reorder, sleep/wake, and process-kill recovery. |
| T05 | `activity.swap` is independent of Offer, Receive, and Replace. New snapshot, Prepare, and unknown decisions require the current peer-relative grant. New Prepare also requires exact authenticated peer/local placements and two active normal-sensitivity Activities. After revocation, only exact Operation/correlation/peer-bound recorded Prepare replay or decision convergence reaches the core endpoint. | Desktop exact-selection confirmation, physical same-session revocation observation, and independent authorization review. |
| T08 | Swap envelopes require negotiated protocol 1.1; protocol 1.0 construction/decoding and Swap-channel lookup fail closed while non-Swap 1.0 traffic remains available. Six fixed-ID canonical frames commit complete JSON and SHA-256 hashes; each request and result schema has hostile-field/binding tests. | Cross-version packaged peers and an independently implemented compatibility reader. |
| T10 | Snapshot discloses only one explicitly named eligible Activity over the encrypted authorized session; rejection contains no Activity. It never becomes inventory, discovery, receipt, or diagnostics. Prepare carries the two complete descriptors only because each endpoint journal needs exact semantic recovery state. | Crash/minidump/export canary inspection, retention/deletion UI, and independent data-flow review. |
| T11 | Endpoint journal format v2 persists correlation and remote participant beside Operation/reservation/decision evidence; v1 records lacking that binding are unsupported. In-memory and protected endpoints apply the same exact match. Required-field shape, canonical GUID/time encoding, representable successor revision, per-Prepared terminal-decision headroom, purpose-separated authenticated `FSEF` storage, reopen-after-ambiguous-save, strict bounds, and digest checks fail closed. | Rollback protection decision, abrupt power-loss/filesystem testing, live Linux Secret Service, and physical restart. |
| T13 | Unknown Abort still requires current authority before consuming one of 32 endpoint records. Every pending network send or response wait has an independent deterministic deadline; timeout cleanup removes only its exact pending instance before releasing the correlation and closes the session. | Sustained hostile-peer load, rate-limit policy, and packaged resource telemetry. |
| T18 | The coordinator performs only exact read-only snapshots before durable intent, then sends Prepare and the recorded decision. A real authenticated encrypted loopback with one local direct endpoint and one remote durable endpoint converges both catalogs; protocol availability does not expose a Desktop Swap command. | Desktop confirmation/recovery, durable Activity-catalog/native Adapter evidence, physical two-device interruption, and user-visible uncertainty testing. |

This slice proves same-host authenticated transport and durable binding, not a
human-confirmed product Swap or arbitrary application migration. Task 3.3c
remains open until local stress plus the exact-commit Windows/macOS/Ubuntu,
Secret Scan, CodeQL, and downloaded TRX evidence close.

### 5.10 Task 8.1 Activity Group and Scene-definition evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T10 | Scene format v1 has no field for descriptors, payloads, adapter state, Trust, Capability snapshots, session IDs, traffic keys, reservations, or Undo Capsules. Unknown fields—including secret canaries—fail decoding. Group/Scene diagnostic strings omit names, slots, and Activity membership. | Repository/export redaction, packaged crash/minidump inspection, and user deletion in task 8.3. |
| T11 | A frozen canonical JSON fixture/hash, exact format version, lowercase GUIDs, strict enums, positive revisions, required-field set, duplicate/unknown rejection, and malformed-Unicode rejection prevent ambiguous local definitions. | Private atomic repository, migration, rollback policy, and filesystem review in task 8.3. |
| T13 | Groups and Scenes admit 1 through 64 unique Activities; the codec admits at most 32 KiB and depth 8 and rejects over-bound input before publishing an aggregate. | Sustained repository load, pruning/retention limits, and packaged resource telemetry. |
| T18 | A Group-derived Scene binds Group ID/revision and freezes the exact expanded Activity order. A later mutable Group cannot silently change that definition. Scene policies map only to existing Handoff, Move, and Replace safety semantics. | Task 8.2 current-state planning, authorization, Replace confirmation/undo, stale-Group presentation, and per-Activity outcomes. |

This slice is local definition evidence only. It does not authorize
`scene.apply`, persist a repository, execute an Activity operation, or prove a
physical multi-device Scene.

### 5.11 Task 8.2 Scene-apply evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T04 | Preview and approval bind exact Scene ID/revision/digest, expiry, saved item order, child Operation/correlation IDs, explicit exact-source selections, and exact Replace targets. A purpose-scoped exact-ID query never chooses among multiple active sources; selection requires a full repreview. Retry reuses child IDs; a Started item without durable terminal evidence becomes Recovering and stops later work. | Physical packet loss, suspend/restart, and cross-device reconciliation. |
| T05 | Preview is read-only and never authority. Every participating peer must grant current peer-relative `scene.apply`, and every child independently reloads current Trust plus its existing Handoff/Move Offer/Receive or Replace Receive/Replace authorization immediately before protected state, connection, or Adapter use. Revocation of either layer yields a per-item blocker without weakening later independent checks. | Same-session revocation observation on physical supported-platform pairs. |
| T08 | Protocol 1.4 introduces strict bounded source-lookup, exact-slot, remote-child, and result messages bound to authenticated participants, Scene/preview/attempt/child identities, exact evidence, action, and deadline. Protocol 1.0–1.3 rejects the feature before send; hostile unknown/duplicate/wrong-binding/expired messages fault closed. | Cross-version packaged peers and an independently implemented compatibility reader. |
| T10 | Preview, approval, apply journal, result, and diagnostics exclude descriptor payloads, titles in durable records, Trust, session material, exception text, and Undo Capsule content. A remote selected source receives only a payload-free child instruction and sends Activity content directly to the target through the existing end-to-end operation path; canary tests inspect coordinator observations, canonical plaintext, protected files, and visible failure strings. | Packaged crash/minidump and diagnostic-export inspection. |
| T11 | A purpose-separated authenticated bounded apply journal persists the parent binding and each item boundary before progressing. Strict version/schema/bounds/digest checks and reopen-after-ambiguous-save fail closed. | Rollback policy, abrupt power-loss/filesystem testing, and live Linux Secret Service review. |
| T13 | A Scene contains at most 64 sequential items; previews expire, apply attempts/journal history are bounded, cancellation stops at a recorded boundary, and no concurrent fan-out amplifies one request. | Sustained hostile/local load and packaged resource telemetry. |
| T15 | Preview clearly distinguishes source preserved/source closes, blockers, exact destructive targets, expiry/staleness, partial completion, and Recovering. Keyboard and accessible-name tests cover confirmation and results without changing the NOT SHARING indicator. | Native screen-reader, focus rendering, scaling, contrast, and reduced-motion observation. |
| T18 | Exact-destination source state resolves No Change without an operation call. Otherwise a Scene-specific exact-slot query examines occupancy before eligibility filtering and distinguishes Empty, one Eligible Conflict, Opaque protected/ineligible occupancy, and Ambiguous; only Preserve Source plus the eligible conflict can become an exact confirmed Replace target, and filtered Replace inventory can never prove Empty. Occupied Move-plus-Replace blocks because source cleanup followed by target-only undo could remove the incoming Activity's last instance. Replace also requires send-time full snapshot revalidation, protected target preservation, and a returned capsule reference. Explicit compensation invokes only exact committed Preserve-Source Replace capsules in reverse order and never claims whole-Scene rollback. | Native Adapter evidence beyond the semantic tracer, independent safety review, and physical crash/recovery. |

Scene apply remains a best-effort orchestration of existing operations, not an
atomic transaction or authority to expand live Groups. Task 8.3 still owns the
Scene repository and inspect/delete/export lifecycle.

### 5.12 Tasks 6.1-6.2 portable Remote Window control evidence plan

| Threat | Implemented evidence boundary | Remaining evidence |
| --- | --- | --- |
| T05 | Each participant admission, Driver transfer, input attempt, and reconciliation reads one immutable current `CapabilityGrant`; view-only requires `mirror.view`, while Driver eligibility/use requires both `mirror.view` and `mirror.drive`. The Desktop picker uses the same role-scoped requirements and a current authenticated connection; `activity.receive` is not Mirror authority, and role/Trust/connection changes clear an ineligible selection. Drive removal returns authority to the host before downgrade; view removal returns authority and removes the peer before local disconnect. A failed local disconnect retains peer-scoped pending cleanup, and later explicit disconnect or reconciliation retries it without re-admitting the peer. | Compose persistent Trust/session revocation with the controller and observe same-session revocation on physical supported-platform pairs. |
| T06 | The controller composes the immutable `MirrorSession`, serializes normal input/transfer, publishes the higher lease epoch before new-driver input, returns expiry/disconnect to the host, rechecks the epoch before and after the input boundary, and preempts pending start/input through local Emergency Stop. Seeded public-interface transitions prove retired epochs never reach input; protocol 1.5 binds the authenticated live Session and rejects stale/wrong bindings before controller use. | Native input enforcement, sleep/wake, and physical disconnect/drop/replay testing. |
| T07 | Initial capture requires a fresh Safe observation and generation-bound admission confirmation. A semantic source remains an active Activity; a generic source requires a current opaque token/source-generation lease and owner/Session-bound native boundary. Capture and a whole input batch acquire an exact source/geometry use scope immediately before the native call. Registry removal marks the lease stale and closes new admission under the registry lock before catalog removal; callbacks drain outside that lock. Only display name updates in place. Owning-application, geometry, capture/input-support, or protection changes retire the exact source and require fresh registration, preventing capture/input split-brain. Source loss publishes sticky `Unavailable`, closes frame admission, and stops capture/input/sessions before external invalidation returns; a stale controller cannot Reset to Idle. Frame delivery requires Active/confirmed/fresh-Safe state, and a terminal sink fault publishes `Unavailable` plus gate stop. Post-boundary rechecks are cleanup evidence only. Protection state commits before observers, delivers revisions through one bounded drainer, and turns notification pressure into `Unknown`; observer failure or blocking cannot preserve an older Safe state. | Continuous native probes and frame-by-frame blank/pause evidence for Windows secure desktop/protected capture, macOS secure input/protected windows, and Wayland portal/PipeWire. |
| T10 | Sharing snapshots/results keep bounded Activity/source display state, participant IDs/roles, lease metadata, protection kind, and stable reason codes only. The unpredictable native token is JSON-ignored and redacted in display strings; the exact native-use contract contains no title, owning-application label, native handle, or process identity. Input events/batches omit coordinates/keys, owned frames omit pixels, and adapter exception text reduces to `local_boundary_exception`. | Packaged crash/minidump, native capture/codec/render logging, diagnostic-export canary inspection, and peer/export projection review. |
| T13 | One controller is bound either to one active semantic Activity or one retained current generic-source lease. The in-memory source registry admits at most 128 total visible or retained-invalidating states; a deferred use/callback drain continues to consume capacity until it completes. Geometry, display metadata, frames, and input batches have explicit bounds; a frame owns one exact pixel plane up to 64 MiB. Native frame handoff permits one in-flight and one latest pending frame, rejects non-advancing sequence, drops protection-blocked frames while retaining the exact binding, disposes every rejected/replaced frame, and reports one typed terminal fault to controller authority. Protection notifications admit eight queued revisions and coalesce overflow to fail-closed `Unknown`. Active participants and failed peer-disconnect cleanup share one 16-slot session budget. | Sustained packaged hostile callback/frame load, resource telemetry, and physical bandwidth/latency measurement. |
| T14 | The portable snapshot exposes lifecycle, capture confirmation, current Driver, participant count, and revision for the headless Desktop sharing candidate. Emergency Stop changes that state, closes frame admission without waiting for downstream work, then calls every local gate without peer acknowledgement. Ordinary Stop closes producers before draining an in-flight frame; Reset returns a named pending result instead of waiting on a blocked delivery. A frame callback is a registered controller lifetime operation. Disposal from that callback returns after fail-close so Stop/source-loss cannot wait cyclically on destination re-entry, while top-level external disposal joins the initial fail-close, every registered operation, and a three-state finalization-completion barrier before releasing borrowed resources. Controller operations and fail-close/finalization sequences, frame deliveries, protection and Emergency callbacks, source uses, and invalidation callbacks share one process-wide, per-invocation active ancestry. A wait first proves the target needs drain; controller and boundary drains yield to shared ancestry, while sinks yield to frame ancestry so ordinary Stop still joins its owned delivery. This directed rule breaks same-kind and cross-component disposal cycles without exempting a top-level external disposer; stale copied contexts rejoin later drains after their token deactivates. The last registered operation atomically claims finalizer ownership before waking external waiters, preventing a finalizer/source-callback circular drain; disposal also closes new source-callback admission before the zero-operation observation. Native Emergency registration is one-shot and owner/Session-bound. Shared ancestry preserves external drain, clears callback references, and blocks replacement while required. Removed source states remain registry-owned until drain completion; cross-source display updates fail instead of waiting under active foreign use ancestry. Reset fails closed while any current-generation attempt is still running or the native lease is stale. | Native hotkey/action and loss, packaged screen-reader behavior, and physical peer/network/UI failure observation. |

These tests use deterministic local boundaries. They do not establish that a
screen frame was captured or blanked, native input was injected or stopped, an
operating-system permission was granted, a peer session was encrypted, or a
physical Device disconnected.

### 5.13 Task 6 authenticated Remote Window control and bounded media evidence plan

| Threat | Required implementation evidence | Remaining evidence |
| --- | --- | --- |
| T04 | A protocol-1.6 attachment request and acknowledgement bind the directed Device pair, exact route, Session, Activity, and both fresh nonces under the purpose-separated media session. The registry atomically reserves a route ID at registration, consumes a matched route once, and independently remembers up to 512 route IDs and 512 initiator-nonce fingerprints for the maximum route lifetime. Cleanup cannot make a consumed route reusable inside that window; an attached route remains reserved until its owner closes. Replay, second claim, stale acknowledgement, wrong protected binding, or full history fails closed without reviving or evicting a security-relevant entry. | Production-listener replay/drop observation on physical peers, long-running churn/resource telemetry, and independent protocol review. |
| T05 | Protocol-1.5 admission, Driver, input, disconnect, and state messages bind the authenticated peer, live Session, exact Activity, correlation/deadline, and applicable epoch. The host adapter delegates to the existing controller so every use still reloads current Capabilities. | Persistent Trust/session revocation composition and physical same-session observation. |
| T06 | Driver requests name the last-known epoch; input names the exact current epoch; state replies carry the resulting higher epoch without echoing input. Wrong/stale/unsolicited bindings fault the control session before authority reaches the controller. | Native input enforcement, sleep/wake, UI recovery, and physical replay/drop evidence. |
| T07 | State replies publish protection lifecycle, capture confirmation, protection kind, and revision. Media Session/Activity binding and channel closure prevent a stale stream from silently continuing after a new live session. | Continuous native protected-surface probes and frame-by-frame blank/pause evidence. |
| T08 | The protocol-1.6 `FSM1` request is exactly 200 bytes and its acknowledgement exactly 232 bytes. The clear prefix exposes only a versioned purpose and 16-byte route locator; it is not a credential and cannot authorize or decrypt. Exact protected bindings, 32-byte nonces, zero flags, no trailing bytes, and clear/protected agreement are mandatory. JPEG decoding admits only one complete TopLeft still image within the 1-MiB encoded, 16,384-per-dimension, 16,777,216-pixel, and 64-MiB BGRA limits before allocating pixels; malformed, truncated, animated, other-format, concatenated, or trailing input fails closed. | Cross-implementation readers, sustained packaged hostile-input runs, and physical same-listener observation. |
| T10 | Remote input remains strict control and is never echoed. Binary media is absent from canonical JSON and structured diagnostics; tests use canaries to inspect exception/result text. | Native pipeline/crash dump/export inspection and independent data-flow review. |
| T11 | Media keys are HKDF purpose-separated from authenticated control keys; AEAD authenticates both the attachment bindings and a strict binary Session/Activity/kind/sequence/chunk header plus payload. Tamper, unknown fields/kinds, invalid lengths, duplicate control fields, and protocol-1.4 frame or protocol-1.5 attachment downgrade fail closed. The media `SecureFrameSession` has no live rekey: its attachment and owning authenticated control connection must close before a directional epoch budget or sequence/epoch boundary is exceeded, and only a fresh authenticated control handshake may derive the next media session and route. | Independent cryptographic review, cross-implementation reader, and physical/packaged reconnect observation. |
| T13 | Fixed frame/chunk, per-peer/session queue/byte/peer, receive-rate, and write-timeout limits reject before unbounded allocation. The process-local route registry defaults to 32 owned routes with a 128 hard cap, 30-second default/two-minute maximum TTL, separate 512-entry nonce and consumed-route histories, one claim per route, and a two-second default/ten-second maximum handshake timeout. History capacity fails closed and recovers only after bounded expiry; live attachments retain their route slot until cleanup. Registry expiry/revoke/dispose races close admission before draining ownership; primary and cleanup failures remain observable. JPEG structure and its equivalent 16,777,216-pixel/64-MiB BGRA limit are checked before pixel allocation; encoded/decoded owners zero managed buffers on disposal, codec-owned Skia spans/native data are cleared before release, and the bounded encode scratch is pooled and clear-on-return. Cancellation cannot release or zero an encrypted read/write buffer while non-cooperative I/O still borrows it. Every success/failure/cancel/dispose path releases its reservation when ownership truly ends. Neither direction may exceed `2^20` protected frames or 1 GiB plaintext in one media epoch; recovery requires a fresh authenticated control connection, media session, and route. | Desktop runtime composition, sustained packaged load, combined budget-exhaustion/cleanup-fault injection, resource telemetry, and physical bandwidth/latency measurement. |
| T14 | Control never waits for media drain or peer acknowledgement to enact local protection or Emergency Stop; media closure is a downstream consequence of local authority loss. | Desktop indicator/action and physical peer/network/UI failure observation. |
| T15 | Protocol 1.6 alone advertises and accepts the media-route feature. Protocol 1.5 retains its frozen Remote Window control and encrypted-frame formats but cannot transfer media-session ownership, register a route, or attach a production stream; rejection never falls back to a clear or separately trusted media channel. | Packaged cross-version peers and production UI/runtime presentation of the control-only downgrade. |

Task 4's implemented boundary covers the route, attachment codec/registry, JPEG
codec, fixtures, and an authenticated same-host loopback within portable bounds.
Task 5.1-5.4 additionally cover production-listener selection, control-owned
media lifetime, bounded logical-frame transport, and fresh-handshake recovery
after media budget exhaustion. Task 5.5 still owns the complete Desktop runtime.
These managed slices do not prove capture, rendering, native input, interactive
quality, physical network behavior, or a usable emergency action.

### 5.14 Task 6 Desktop Remote Window workflow candidate evidence plan

| Threat | Local candidate boundary | Remaining evidence |
| --- | --- | --- |
| T05 | Both production any-of control profiles admit Mirror-only Trust. A same-process factory loopback proves `mirror.view` establishes an authenticated idle connection and enters only the view-qualified inventory; `mirror.drive` alone can establish the channel but enters no picker. Successful Trust mutations publish after commit and refresh inventory without depending on connection churn; observer failures cannot own the mutation or drain. Another eligible Activity, Scene, or Mirror alternative retains the registered channel, while removal of the final alternative drains it. | Physical same-session grant changes and shutdown across two packaged supported-platform peers, including sleep/wake and credential-store failure. |
| T06 | The persistent Desktop header exposes one synchronous local Emergency Stop for Starting, Active, and ProtectionPaused. The admitted DriverEligible role is independent of the mutable preview checkbox; every non-Granted input state stops an in-flight, preview-started, or snapshot-observed remote DriverEligible path. An `Applied` Start result is reduced before its follow-up refresh, so later read uncertainty retains the newly accepted stop boundary. Cancellation still wins when Start ignores its token and returns success. An inactive observation before a late successful result forces local cleanup unless a replacement session has already begun; controller/safety generation plus inactive provenance prevent the old result from stopping or mutating that replacement. Explicit reset permits a later view-only session without restoring the old Driver. | Native input enforcement, platform-local emergency action/hotkey, peer/network/UI failure on physical supported-platform pairs, and sleep/wake. |
| T07 | Capture must be Granted before start. Denial, revocation, and ungranted state fail closed; undefined state, state-read failure, and request exceptions reduce to named `Unavailable`. A permission `Changed` callback uses bounded cached state and crosses any required synchronous local Emergency Stop before queuing UI presentation; request completion re-reads live state so revocation wins. One atomic safety reducer orders revision, generation, permission, admission, and inactive provenance; no observer or dispatcher runs under its gate. | Windows Graphics Capture, ScreenCaptureKit, Wayland portal/PipeWire and X11 degradation with real grant/deny/revoke and protected-surface evidence. |
| T10 | The Desktop projects only bounded Activity identity/title, target display name, lifecycle, participant count, Driver/epoch, protection, revision, stable status, and per-boundary confirmation. Permission/service/start exception canaries never enter visible text; no frame, raw input, descriptor payload, credential, key, or native handle enters this state. | Packaged crash/minidump, native adapter logging, diagnostic-export, and screen-reader inspection. |
| T13 | Permission and Start operations are independently serialized and share view-model lifetime cancellation. Disposal first crosses local Emergency Stop, then starts cancellation/unsubscribe/drain while failures cannot skip later cleanup and concurrent callers share completion. Permission-busy and associated command notifications register their external-boundary lease before invoking observers outside the safety gate; synchronous observer disposal excludes that callback lease from its own drain while external callers still join cleanup. Shell closes projection leases and initiates Remote Window and pairing safety teardown in parallel before dependency release. Pairing retains cleanup-unconfirmed session ownership, blocks re-enable, publishes no observer under its lifecycle gate, and shares disposal completion. Selection/role changes cannot relabel an admitted request or receive its late result. | Non-cooperative native API teardown, synchronous observer-triggered disposal, sustained start/cancel churn, resource telemetry, and real process/window termination. |
| T14 | Starting, Active, ProtectionPaused, EmergencyStopped, Unavailable, and inactive have distinct non-color text and accessible names. A transient unavailable read preserves the Activity/revision watermark and last-known local stop; explicit null/Idle establishes the same-Activity lower-revision boundary. A controller `Unavailable` after failed start can be reset for retry only from an accepted snapshot proving capture Stopped, no remote participant, and no Driver; transient service unavailability and unconfirmed stops cannot qualify. | Packaged native screen-reader, high-contrast, large-text, reduced-motion, and physical UI-failure observation. |
| T17 | Capture rationale names visible screen exposure and the OS revocation route before an acknowledged request. Input/accessibility rationale and request are unavailable until the user explicitly enables remote driving; review/cancel performs no privileged operation. | Matching Windows/macOS/Linux native prompt, settings navigation, grant/deny/revoke, and post-revocation behavior. |

Headless UI, deterministic fakes, and hosted OS contracts prove Desktop ordering,
state, exception reduction, and accessibility wiring only. They do not prove an
operating-system permission, captured frame, injected input, protected surface,
native emergency action, physical peer, or usable packaged screen reader.

### 5.15 Task 5.5a protocol-1.7 Remote Window Preparation evidence plan

| Threat | Required Task 5.5a evidence | Remaining evidence |
| --- | --- | --- |
| T04 | `remote-window.prepare` and `remote-window.ready` repeat one exact correlation, Session, Activity, directed Device pair, frozen role, UTC deadline, and domain-separated canonical SHA-256 `prepareDigest`. Both peers recompute and constant-time compare the digest. Each control registration owns at most one pending transaction and retains a terminal tombstone through the deadline or connection close; unknown, duplicate, conflicting, cross-request, expired, or delayed Ready faults closed without reviving work. A locally produced Rejected response is committed before its delivery-dependent fail-close, so the host observes the bounded reason before closing the owning connection. The failed connection generation becomes unavailable for reacquisition, retry, route, or media operations immediately. A request-bound watchdog accepts only the exact same request with a positive remaining deadline of at most 10 seconds, survives lease disposal, and fail-closes at that original deadline if the host does not. Conflicting, expired, overlong, or provider-setup-failed deferral does not poison or extend the generation; explicit close and deadline expiry share one cleanup, while owner revocation cancels the watchdog. A test-only coordinator clock proves the production currentness check treats equality with the request deadline as expired after successful `FSM1` and Ready, without reviving the generation. | Independent protocol review, cross-implementation fixtures, and physical loss/replay observation. |
| T05 | Only the source host checks its current peer-relative `mirror.view` and optional `mirror.drive` grant to the participant before Prepare, before capture, and through AddParticipant. The participant verifies current authenticated Trust/connection, local recipient and receive policy, and readiness without requiring a reciprocal Mirror grant, which would authorize the opposite source direction. v1 defines no `remote-window.receive` Capability. A managed production-path tracer now covers complementary one-way success, reversed-grant denial, and same-session Mirror grant downgrade with active-session drain. | The complete direction/fault matrix and packaged physical one-way-grant and revocation evidence. |
| T06 | Prepare, Ready, permission, route possession, attachment, and renderer readiness grant no membership, capture, Driver, input, or rendering authority. Ready success only permits host revalidation; protection/Emergency ownership, controller Start, and exact AddParticipant occur with frame admission closed. Only correlated state with action Admission, outcome Applied or AlreadyApplied, exact role, and current media binding establishes the participant's known binding and opens frames. The managed tracer observes zero capture before Prepare/Ready and attachment complete, zero media/render before final Admission, then exercises Driver input and Emergency Stop. Three renderer-failure rows own an explicit wait for both real media-session attachment completions before injecting failure; both sessions then carry the exact protocol, Device, Session, and Activity binding, while Admission, capture, media send, and render remain zero. A fourth row blocks the real listener before host directory publication, proves participant attached and host unattached at failure, commits Rejected before fail-close, then publishes host attachment and drains every owner. These test barriers are not production ordering guarantees. The post-`FSM1` expiry case additionally completes Ready and one renderer Prepare before exact deadline equality, yet publishes no Admission or active generation and performs no capture, media send, or render. The caller-cancellation case cancels only the `StartAsync` caller token while the harness remains live and the clock is before deadline; production returns that exact token without Admission, capture, send, or render. A managed active permission-loss observation closes frame admission, invokes the local Emergency Stop boundaries, and drains the admitted session and its owner graph. | Native capture/input enforcement, the remaining complete per-boundary fault matrix, and Emergency Stop under physical peer/network/UI failure. |
| T07 | The host requires fresh Safe protection before Prepare and rechecks it after Ready immediately before Start. Protection loss at either boundary rejects or stops the transaction before frame publication; a Ready result cannot cache Safe state. The active permission-loss tracer drives the managed permission abstraction; it is not evidence of a real Windows, macOS, or Linux permission revocation. | Continuous platform protection probes and frame-by-frame physical blank/pause evidence. |
| T08 | Protocol 1.7 alone accepts the two strict canonical schemas. Prepare and Ready reject unknown, duplicate, null, wrong-type, or trailing fields, malformed digest, wrong authenticated direction/identity, any binding or role mismatch, and inconsistent envelope/body deadlines. Native token/handle/generation, route ID, Descriptor, Kind, raw title, key, input, frame, and exception text are absent. Malformed or wrongly bound input is not reflected in Ready. A managed tracer carries `FSM1`, encrypted media, and JPEG decode through the production listener. The attachment-failure tracer instead uses an authenticated, signed candidate, proves that its verified TCP endpoint accepts the connection, and then immediately resets before the `FSM1` handshake completes; it is attachment-failure evidence, not a malformed-`FSM1` byte test. | Cross-implementation hostile readers, cross-platform native/packaged execution, and packaged physical traffic observation. |
| T10 | Ready rejection uses one allowlisted bounded reason and diagnostics expose only bounded identifiers, phase, and outcome. Prepare/Ready and final state contain no raw source title, native identity, media locator, payload, or exception text. A post-accept TCP reset is projected across the authenticated control path only as `media_attachment_failed`; raw socket text is not reflected to the host. Renderer factory throw and foreign/tokenless cancellation expose only `renderer_start_failed`, while a valid null/Missing result exposes only `renderer_unavailable`. Exact deadline equality exposes only `preparation_expired`. Actual caller cancellation propagates the cancellation family and exact caller token rather than producing a rejection reason. | Native adapter logs, crash/minidump, diagnostics export, and screen-reader inspection. |
| T13 | The single control dispatch loop performs only validation/reservation and starts one owned deadline/lifetime worker before returning. Stop/dispose cancels and joins it. A Ready exposed during Prepare send remains buffered and cannot authorize final Admission until the send commits; result publication shares that commit and is irreversible. Prepare, Ready, and final Admission recheck the absolute deadline at actual wire admission, independent of timer scheduling. Desktop networking shares one connection-owned media directory with the published listener, and the handler exposes an atomic generation-bound Preparation/media lease. A production coordinator consumes that lease with a verified peer-endpoint connector. Ready false, timeout, cancel, revoke, disconnect, attachment, capture, admission, state, renderer, or cleanup failure closes frame admission and attempts every owner cleanup. Terminal authenticated-control disconnect, same-session capability revocation, managed active permission loss, attachment reset after proved TCP accept, and renderer throw/null/foreign-cancellation converge the active coordinator snapshot, media budget, both media directories and routes, renderer, and authenticated-control owners to zero. Attachment failure enters neither media-attachment wait nor Admission, capture, or render. Three renderer-start rows explicitly wait for bilateral attachment before injecting failure; they do not imply that responder directory publication naturally precedes participant renderer entry. A fourth row freezes that exact earlier window and proves Rejected-before-fail-close plus complete post-publication cleanup. The exact-deadline test waits for media attachment once, then host fail-closes and disposes once without publishing Admission or an active generation; renderer, route, directory, handler, lease, channel, and control owners drain, and the old generation cannot be reacquired. The caller-cancellation test independently keeps the harness alive, observes fail-close and Dispose once, and drains the same owners. Actual linked cancellation and deadline remain eager. Renderer primary failure stays observable with cleanup or lifecycle failure, and explicit/deadline fail-close shares one cleanup. | The remaining complete per-boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix, combined failure injection, sustained packaged churn, non-cooperative native teardown, and resource telemetry. |
| T14 | Participant readiness never opens rendering. The final accepted Admission state is the first rendering gate, while the host's persistent sharing indicator and independent Emergency Stop begin with the actual native session. Rejection leaves source execution unchanged and no hidden capture. The managed tracer confirms that Ready/attachment do not render, final Admission does, and local Emergency Stop does not await network acknowledgement. | Persistent UI indicator, packaged accessibility, and physical UI/network failure observation. |
| T15 | Protocol 1.5 remains frozen control/encrypted-frame behavior, 1.6 adds only frozen `FSM1`, and 1.7 adds Preparation. Negotiating below 1.7 rejects Prepare/Ready and never falls back to Activity transfer, unsolicited state, clear media, or unprepared Admission. The managed tracer exercises 1.7; existing protocol tests cover lower-version rejection. | Packaged mixed-version presentation and physical downgrade observation. |

Commit `80191d6`, retained by `761ac75`, provides six narrow managed same-host
production-path tracer scenarios: successful DriverEligible
media/input/Emergency Stop; reversed-grant denial; terminal
authenticated-control disconnect; same-session Mirror capability revocation;
managed active permission loss (`Granted` to `Denied`); and a signed, verified
candidate whose TCP endpoint is proved accepted before reset interrupts the
`FSM1` attachment. `ca63874` also proves that an accepted TCP connection whose
attachment fails preserves the preparation response-before-close ordering
without changing ordinary media fail-close. Local exact-tree Debug and Release
verification each pass 2210/2210 solution tests, including Desktop 535/535 and
Transport 688/688.

Exact-SHA hosted CI `33246518217` passes the same `2210/2210` managed and
contract tests on Windows, macOS, and Linux plus Secret Scan and all three
reproducible unsigned package jobs; CodeQL `33246518202` also passes. These
hosted results do not establish native API, real permission, physical-device,
signed-package, or notarization behavior.

Subsequent implementation checkpoint
`fde38b2bae9d02f177fd86e22a8beecb060325e9` expands the tracer to nine cases by
adding renderer factory throw, valid null/Missing, and foreign or tokenless
`OperationCanceledException` after successful authenticated protocol-1.7 and
`FSM1`. Local macOS arm64 verification with .NET SDK 10.0.301 passed both
warning-as-error builds and both complete solutions at `2232/2232`, including
Desktop `544/544` and Transport `701/701`. Ten fresh processes in each
configuration passed the three-case renderer theory (`60/60` case executions),
while focused lease and media-session suites passed `16/16` and `28/28` in each
configuration. Format, diff, dependency-vulnerability, explicit TEST MODE
composition, and simulator checks passed. No local `gitleaks` result exists.
Exact-SHA CI `33249181870` and CodeQL `33249181871` for this subsequent tree
both succeeded. Downloaded Windows, macOS, and Linux artifacts each contain 12
TRX files summing to `2232/2232`, with every non-success counter zero. Secret
Scan and all three reproducible unsigned package jobs also passed. The final
strict review reported no P0, P1, or P2 findings in this implemented slice;
that internal review and hosted managed evidence are neither an external
security audit nor native/physical evidence.

Test-only commit `0f1f32d0e8ea251194755a5b4d150d3e294433ff` adds no
production source change and expands the managed tracer to ten cases with one
post-`FSM1`, pre-Admission deadline-equality timeout. Local macOS Debug and
Release focused expiry runs passed `1/1`; the full tracer class passed `10/10`;
warning-as-error builds completed with zero warnings and errors; and both
solutions passed `2233/2233`, including Desktop `545/545` and Transport
`701/701`. Format, diff, dependency-vulnerability, explicit TEST MODE
composition, and simulator checks passed. Internal strict review reported no
P0/P1/P2 finding but is not an external audit. Superseding exact SHA
`e504c839cac2e45a4ca7ad17316c8278e4928c2e` passed CI `33250747660` and
CodeQL `33250747671`; every hosted OS passed `2233/2233`, and Secret Scan plus
all reproducible unsigned package jobs passed. These remain hosted managed
contract and packaging results, not native or physical evidence.

At the expiry checkpoint, only one post-attachment timeout example was covered;
actual caller cancellation, cleanup-fault coverage, and the complete
per-boundary matrix remained open.

Test commits `45e2d494501167712ec4abdff69d8d232f355d14` and
`5bb6d0863033c3b6668335e15d6a6fe336ee46a7` add no production source change and
expand the managed tracer to eleven cases with one actual caller cancellation.
Local focused Debug/Release passed `1/1`, the tracer class passed `11/11`, twenty
fresh Debug processes passed `20/20`, both warning-as-error builds passed, and
both solutions passed `2234/2234`, including Desktop `546/546`, Platform
`219/219`, and Transport `701/701`. Other gates and strict review passed with no
P0/P1/P2 finding; that review is not an external audit. Exact-SHA hosted CI and
CodeQL for `5bb6d08` succeeded in runs `33251741558` and `33251741546`.
Windows, macOS, and Linux each passed `2234/2234`; Secret Scan and all
reproducible unsigned package jobs also passed. These are hosted managed
contract and packaging results, not native/physical evidence. This is one
post-`FSM1`, pre-Admission caller-cancellation case; cleanup-fault and the
complete per-boundary matrix remain open.

Docs SHA `908a04a2f465bccccf56b72fd36cb5f048506a63` exposed a Linux
renderer-tracer sampling race in CI `33254082958`: the initiator could enter the
renderer factory after validating FSM1 acknowledgement but before the responder
listener published its attached session into the host directory. Test-only
commit `ac48ec3aa88aa78f736b5550bc778a5ff4e95abb` now explicitly awaits both
real session attachment completions before injecting renderer failure. Local
Debug and Release solutions passed `2234/2234`, focused eight-way pressure
passed `120/120`, and strict review found no P0/P1/P2. Exact-SHA CI
`33254883850` and CodeQL `33254883851` succeeded; every hosted OS passed
`2234/2234`, and Secret Scan plus all three unsigned package jobs passed. This
is test synchronization, not a production ordering or new security control.

Test-only commit `58569be3215bbb38a6767398d28c3f428130601a` then freezes the
valid earlier acknowledgement-to-host-publication window. The real listener
handler is blocked after route attachment but before forwarding to the host
directory; the participant is attached and the exact host session is observed
but unattached. Renderer failure commits and the host validates a real Rejected
response before coordinator fail-close or Dispose. The test then publishes the
real host attachment, returns Rejected, observes fail-close and Dispose once
each, and proves the complete owner graph drains to zero with no Admission,
capture, send, or render. It does not claim that fail-close itself occurs while
publication is still blocked. Local Debug/Release solutions passed `2235/2235`;
the final four-row pressure passed `160/160`; strict review reported no P0/P1/P2. Exact-
SHA CI `33256672974` and CodeQL `33256672962` succeeded; all hosted OSes passed
`2235/2235`, and Secret Scan plus all three unsigned package jobs passed. This
closes only that renderer-failure row, not the remaining complete fault matrix.

The complete per-boundary reject/throw/cancel/timeout/revoke/disconnect/
cleanup-fault matrix remains open. Tasks 5, 5.5a, and 5.5, all native and
physical-device evidence, packaged accessibility, signed/notarized release
gates, and the long-running Goal also remain open. These managed local tests do
not establish real operating-system permission revocation or Windows/Linux
execution, and no v1 release criterion is closed by this slice alone.
`CreateProduction()` must continue to report Remote Window unavailable.

## 6. Security state machine rules

- `Discovered` is never equivalent to `Paired`.
- `Paired` is never equivalent to capability-authorized.
- Capabilities are evaluated for every new operation and driver lease. A revoked
  Swap may only finish through Exact Recorded Decision Convergence bound to the
  durable Operation/correlation/peer record; this is not authority for new work.
- Trust revocation removes authorization and active-session eligibility before
  waiting for shutdown; every affected registered session receives a stop
  request before the revocation call returns, with failures surfaced.
- Reconnection creates a new secure session and key epoch.
- Unknown identity, version, protection state, transaction outcome, or lease
  epoch cannot authorize capture or input.
- Remote Window Prepare and Ready are pre-admission facts. Only the exact host
  controller's final accepted Admission state may establish participant/render
  authority, and the source host alone interprets its grant to that participant.
- A failed or terminal Remote Window Preparation cannot be retried on its
  connection-owned media session; recovery requires a fresh authenticated
  connection and binding.
- A rejected Preparation commits its allowlisted response before
  delivery-dependent connection cleanup. The failed generation is immediately
  ineligible for reuse, and the original deadline remains a participant-side
  fail-close fallback if the host does not close the connection. Primary and
  cleanup failures must both remain observable.
- Emergency stop is local, synchronous at the platform boundary, and does not
  wait for a network/UI round trip.

## 7. Sensitive data retention

Descriptor payloads exist only as long as required by the operation/undo policy.
Receipts keep opaque IDs, kinds, sizes, hashes, outcome, and reason codes. Trust
and history deletion is local and immediate; peer notification is best effort.
Diagnostic exports are generated into a new user-selected file, scanned for
registered canary secrets in tests, and never uploaded automatically.

## 8. Platform-specific review gates

- **Windows**: secure desktop behavior, protected capture flags, DPAPI/Credential
  Manager scope, SendInput integrity levels, and emergency-stop hotkey.
- **macOS**: Screen Recording and Accessibility TCC behavior, secure input,
  Keychain access groups, hardened runtime, and notarization.
- **Linux**: Wayland portal consent, PipeWire node lifetime, RemoteDesktop portal
  revocation, Secret Service availability, and explicit risk messaging for X11.

Mocks prove core behavior only. Each native gate needs matching-runner tests and
documented real-machine exploratory evidence before v1 acceptance.

## 9. Security release blockers

- unreviewed production cryptographic protocol;
- plaintext identity private keys or Activity content;
- any path for hidden capture/input;
- emergency stop dependent on peer response;
- known critical/high dependency vulnerability without documented mitigation;
- missing negative authorization or malformed-input tests;
- diagnostics that can contain test canary secrets.
