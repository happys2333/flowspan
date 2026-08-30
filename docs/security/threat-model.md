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
| T06 | Remote input or capture after authority/permission loss | Monotonic driver lease epochs, short expiry, local enforcement, emergency-stop epoch bump; permission revocation and start admission share one synchronized fact so revocation either rejects the crossing or stops the admitted start; pre-Prepare additionally reserves the exact prompt-free permission owner/revision and frozen-role facts under the adapter's accepted-observation gate, so a later accepted change makes the old reservation terminal and regrant cannot revive it; Remote Window Prepare, Ready, and media attachment grant no capture, participant, Driver, input, or render authority, frame admission remains closed through host revalidation/Start/AddParticipant, and only exact final Admission state establishes the participant binding; failed, throwing, or cancelled capture admission attempts synchronously invoke local capture cleanup even when an adapter ignores cancellation and returns success, and never claim `Stopped` without confirmation; generation and inactive-boundary provenance stop an orphaned late successful Start without stopping a replacement session | lease model/property tests plus deterministic permission/start admission, prompt-free reservation/commit ordering, Ready-before-capture and final-state-before-frame tests, cancellation-ignoring success, inactive-before-success, replacement-session isolation, and failed-start cleanup races |
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
| T05 | Only the source host checks its current peer-relative `mirror.view` and optional `mirror.drive` grant to the participant before Prepare, before capture, and through AddParticipant. The participant verifies current authenticated Trust/connection, local recipient and receive policy, and readiness without requiring a reciprocal Mirror grant, which would authorize the opposite source direction. v1 defines no `remote-window.receive` Capability. The host now reserves the exact authenticated-handshake fingerprint plus all role-required Mirror Capabilities under the Trust mutation gate before route admission; every Applied revoke or Capability update invalidates that exact Preparation before ordinary observers or active-session Stop. A managed production-path tracer covers complementary one-way success, reversed-grant denial, same-session active Mirror downgrade, and one pre-Prepare Applied same-grant `R < M < S` invalidation with zero Prepare wire and complete drain. | The other production-composed Authorization orders, complete direction/fault matrix, and packaged physical one-way-grant and revocation evidence. |
| T06 | Prepare, Ready, permission, route possession, attachment, and renderer readiness grant no membership, capture, Driver, input, or rendering authority. Ready success only permits host revalidation; protection/Emergency ownership, controller Start, and exact AddParticipant occur with frame admission closed. Only correlated state with action Admission, outcome Applied or AlreadyApplied, exact role, and current media binding establishes the participant's known binding and opens frames. The managed tracer observes zero capture before Prepare/Ready and attachment complete, zero media/render before final Admission, then exercises Driver input and Emergency Stop. Three renderer-failure rows own an explicit wait for both real media-session attachment completions before injecting failure; both sessions then carry the exact protocol, Device, Session, and Activity binding, while Admission, capture, media send, and render remain zero. A fourth row blocks the real listener before host directory publication, proves participant attached and host unattached at failure, commits Rejected before fail-close, then publishes host attachment and drains every owner. A fifth row lets fail-close and the coordinator/control/directory/route graph drain while one listener handler remains blocked, then proves the delayed attachment fails and the handler settles without resurrection. These test barriers are not production ordering guarantees. The post-`FSM1` expiry case additionally completes Ready and one renderer Prepare before exact deadline equality, yet publishes no Admission or active generation and performs no capture, media send, or render. The caller-cancellation case cancels only the `StartAsync` caller token while the harness remains live and the clock is before deadline; production returns that exact token without Admission, capture, send, or render. A managed active permission-loss observation closes frame admission, invokes the local Emergency Stop boundaries, and drains the admitted session and its owner graph. | Native capture/input enforcement, the remaining complete per-boundary fault matrix, and Emergency Stop under physical peer/network/UI failure. |
| T07 | The host binds one exact fresh-Safe Protection observation and its complete freshness interval before route admission. The same registration remains temporary through Prepare, Ready, and attachment, becomes formal immediately before host-reservation promotion, and becomes live only after a fresh post-`Starting` source-gate admission immediately before capture. Live mutation closes new frame/input admission under the source gate, drains exact in-flight uses before reopening, and preserves ordered observations through a bounded fail-closed queue. A managed tracer proves only Protection `R < M < S` for `SecureInput` and `Unknown`; it does not instantiate a native protection probe. | Production-composed `M < R` and `S < M`, the complete Protection fault matrix, continuous platform protection probes, and frame-by-frame physical blank/pause evidence. |
| T08 | Protocol 1.7 alone accepts the two strict canonical schemas. Prepare and Ready reject unknown, duplicate, null, wrong-type, or trailing fields, malformed digest, wrong authenticated direction/identity, any binding or role mismatch, and inconsistent envelope/body deadlines. Native token/handle/generation, route ID, Descriptor, Kind, raw title, key, input, frame, and exception text are absent. Malformed or wrongly bound input is not reflected in Ready. A managed tracer carries `FSM1`, encrypted media, and JPEG decode through the production listener. The attachment-failure tracer instead uses an authenticated, signed candidate, proves that its verified TCP endpoint accepts the connection, and then immediately resets before the `FSM1` handshake completes; it is attachment-failure evidence, not a malformed-`FSM1` byte test. | Cross-implementation hostile readers, cross-platform native/packaged execution, and packaged physical traffic observation. |
| T10 | Ready rejection uses one allowlisted bounded reason and diagnostics expose only bounded identifiers, phase, and outcome. Prepare/Ready and final state contain no raw source title, native identity, media locator, payload, or exception text. A post-accept TCP reset is projected across the authenticated control path only as `media_attachment_failed`; raw socket text is not reflected to the host. Renderer factory throw and foreign/tokenless cancellation expose only `renderer_start_failed`, while a valid null/Missing result exposes only `renderer_unavailable`. Exact deadline equality exposes only `preparation_expired`. Actual caller cancellation propagates the cancellation family and exact caller token rather than producing a rejection reason. | Native adapter logs, crash/minidump, diagnostics export, and screen-reader inspection. |
| T13 | The single control dispatch loop performs only validation/reservation and starts one owned deadline/lifetime worker before returning. Stop/dispose cancels and joins it. A Ready exposed during Prepare send remains buffered and cannot authorize final Admission until the send commits; result publication shares that commit and is irreversible. Prepare, Ready, and final Admission recheck the absolute deadline at actual wire admission, independent of timer scheduling. Desktop networking shares one connection-owned media directory with the published listener, and the handler exposes an atomic generation-bound Preparation/media lease. A production coordinator consumes that lease with a verified peer-endpoint connector. Ready false, timeout, cancel, revoke, disconnect, attachment, capture, admission, state, renderer, or cleanup failure closes frame admission and attempts every owner cleanup. Terminal authenticated-control disconnect, same-session capability revocation, managed active permission loss, attachment reset after proved TCP accept, and renderer throw/null/foreign-cancellation converge the active coordinator snapshot, media budget, both media directories and routes, renderer, and authenticated-control owners to zero. Attachment failure enters neither media-attachment wait nor Admission, capture, or render. Three renderer-start rows explicitly wait for bilateral attachment before injecting failure; they do not imply that responder directory publication naturally precedes participant renderer entry. A fourth row freezes that exact earlier window and proves Rejected-before-fail-close plus complete post-publication cleanup. A fifth keeps directory publication blocked while the coordinator/control/directory/route/lease graph drains and one listener handler remains active, then proves the late attachment is rejected as stale, the handler settles, and ownership cannot resurrect; that renderer row itself does not construct a replacement generation or prove ABA resistance. A companion Transport contract independently drains that old generation, prepares a replacement route for the same Device pair, Session, and Activity with a fresh Route ID, completes real `FSM1` acceptance so the route is Attached while directory publication remains gated, rejects the delayed old exact binding without affecting the replacement, then attaches the replacement and transfers encrypted media. Initiator acknowledgement verification alone does not prove responder host-directory publication; six renderer-rejection fixtures now assert the rejection first and use a bounded cancellable attachment-publication barrier before cleanup, without creating a production ordering guarantee. A fourteenth managed row injects one Emergency Stop registration-disposal failure during active authenticated-control disconnect; the same failure remains observable while every later managed owner drains. The exact-deadline test waits for media attachment once, then host fail-closes and disposes once without publishing Admission or an active generation; renderer, route, directory, handler, lease, channel, and control owners drain, and the old generation cannot be reacquired. The caller-cancellation test independently keeps the harness alive, observes fail-close and Dispose once, and drains the same owners. A pending-renderer disconnect row admits Prepare send and bilateral `FSM1`, then loses authenticated control before any Ready outcome while renderer Preparation is non-cooperatively blocked. Owned cleanup enters and cancellation is observed without falsely completing before explicit release; afterward one bounded Rejected result appears, the late renderer is disposed, no later authority opens, and both nodes drain. Actual linked cancellation and deadline remain eager. Renderer primary failure stays observable with cleanup or lifecycle failure, and explicit/deadline fail-close shares one cleanup. | The remaining complete per-boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix, combined failure injection, the remaining replacement/ABA matrix, sustained packaged churn, non-cooperative native teardown, and resource telemetry. |
| T14 | Participant readiness never opens rendering. The final accepted Admission state is the first rendering gate, while the host's persistent sharing indicator and independent Emergency Stop begin with the actual native session. Rejection leaves source execution unchanged and no hidden capture. The managed tracer confirms that Ready/attachment do not render, final Admission does, and local Emergency Stop does not await network acknowledgement. | Persistent UI indicator, packaged accessibility, and physical UI/network failure observation. |
| T15 | Protocol 1.5 remains frozen control/encrypted-frame behavior, 1.6 adds only frozen `FSM1`, and 1.7 adds Preparation. Negotiating below 1.7 rejects Prepare/Ready and never falls back to Activity transfer, unsolicited state, clear media, or unprepared Admission. The managed tracer exercises 1.7; existing protocol tests cover lower-version rejection. | Packaged mixed-version presentation and physical downgrade observation. |

Additional current T13 managed evidence composes the previously separate stale-
attachment and replacement boundaries through one causal Desktop trace. After a
renderer-failed generation drains with its listener handler gated, the same
Device pair reconnects under strictly higher control generations and fresh
Session, Correlation, and Route IDs. Releasing only the stale exact binding
cannot attach to, stop, or admit the replacement; releasing the replacement's
independent gate permits Admission and encrypted render before full teardown.
This is the fifteenth managed tracer case, not the complete T13 replacement or
fault matrix.

The sixteenth managed tracer case adds T10/T13 evidence for one more cleanup
owner. During active authenticated disconnect, capture Emergency Stop clears its
owner and throws once. The injected managed capture-boundary exception is reduced
to the bounded
`capture=local_boundary_exception` result with no inner exception or injected
message, while input and session Emergency Stop still complete. Ordinary Stop and
the remaining owner graph then drain, and terminal failure plus the first
explicitly observed coordinator `DisposeAsync` share the same bounded exception.
This covers one capture cleanup-fault intersection, not
the remaining cleanup matrix or native behavior.

The seventeenth managed tracer case combines that capture-boundary projection and
the registration-disposal failure during one authenticated disconnect. Final
terminal state is a two-inner `AggregateException`: the bounded capture
projection first and the exact raw registration exception second. The first
explicitly observed coordinator `DisposeAsync` shares that outer instance, the
capture canary remains absent, and the complete owner graph drains. This closes
one managed combined-failure cross-product, not the remaining combinations or
native behavior.

The eighteenth managed tracer case adds a distinct T10 input-boundary row. The
managed input Emergency Stop applies before a one-shot injected throw;
production exposes only `input=local_boundary_exception`, with capture and
sharing-session confirmation, a null inner exception, and no injected canary.
The nineteenth row adds a late T13 owner boundary: the wrapper first awaits the
real authenticated host connection's disposal and observes it non-current, then
throws once. That raw cleanup exception remains visible by identity. In both
rows `TerminalFailure` and the first explicitly observed coordinator
`DisposeAsync` share the same failure instance, and the complete owner graph
drains. These are managed fault-injection results, not native input or physical-
disconnect evidence.

The twentieth row covers the distinct shared host fail-close Task. The wrapper
awaits real inner fail-close before one injected throw; the immediate terminal
path and later cleanup reuse one completion, expose the exact raw exception, and
continue through final connection disposal and complete owner drain. The twenty-
first row combines registration disposal and host-connection disposal inside one
cleanup pass. Its terminal failure is one flat two-inner aggregate in cleanup
order, preserving both exact exception identities. These are managed lifecycle
faults, not physical network-loss evidence.

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

Test-only commit `63a52e5e7d2cbba7555a084bc6fa389dba6b5dd9` adds the next
renderer cleanup-race row without changing production source. Rejected returns,
Start/fail-close/Dispose finish, and the coordinator/control/directory/route/
lease graph drains while one real listener handler remains blocked before host
directory publication with `ForwardCount == 0`. Releasing that gate later
produces the expected
`MediaAttachment`-stage `InvalidDataException` for an attachment with no live
owning control connection, followed by a second zero-owner observation. No
Admission, capture, send, or render occurs, and no owner resurrects. The row
does not create a replacement generation and therefore is not ABA evidence.

The TDD RED passed four rows and failed only the new fifth row after 29 ms. The
final focused Debug/Release theory passed `5/5`, the tracer class passed `13/13`
in both configurations, and 40 fresh eight-way processes passed `200/200` row
executions. Both full warning-as-error builds completed without warnings or
errors; both solutions passed `2236/2236`, including Desktop `548/548`, Platform
`219/219`, and Transport `701/701`. Strict review reported no P0/P1/P2. This is
test orchestration and managed-loopback evidence, not a production defect fix
or new security control.

Validation also exposed two ordering gaps in a pre-existing concurrency test,
not a live-rekey defect. Responder `SendEpoch == 1` sampled after response flush
but before the responder's local epoch advance; initiator `SendEpoch == 3`
showed that the second call could start after the first completed. Test-only
commit `0e573907c30cf34b97339a1dd79ee8d3ca824399` starts both calls before
server receive and uses a marker returned by that receive loop as the responder-
transition barrier. The production send gate already spans response write
through local epoch advance, preventing old-epoch application-frame interleave.
No production source changed, and 200 fresh alternating Debug/Release processes
passed the repaired case.

Exact HEAD CI
[`33259599324`](https://github.com/happys2333/flowspan/actions/runs/33259599324)
and CodeQL
[`33259599282`](https://github.com/happys2333/flowspan/actions/runs/33259599282)
completed successfully. Every hosted OS passed `2236/2236`; Secret Scan,
CodeQL analysis, and all three reproducible unsigned package jobs passed. This
closes only one cleanup-race row; the remaining complete matrix,
replacement/ABA variants, and all external security evidence remain open.

Test-only commit `ba58562aff020e3cd9fcc5c8066bcfe74d692b8b` adds one
T13 Transport exact-binding ABA contract and no production source change. After
the old authenticated control generation and all published owners drain, an
accepted old attachment remains blocked between route attachment and directory
publication. A higher replacement control generation for the same Device pair
prepares a route for the same Session and Activity with a fresh Route ID, then
completes real `FSM1` acceptance so the route is Attached while directory
publication stays gated. Releasing only the old attachment must reject its stale
exact binding while the replacement remains current and unstopped, with its host
directory still unattached. The replacement then independently attaches and
transfers encrypted media before final cleanup. This prevents one tested stale
attachment from consuming or retargeting that prepared replacement. It does not
broaden T06 authority, capture, input, or Admission claims and does not expand
the thirteen-case Desktop tracer.

The correct two-gate fact was GREEN against existing production; a shared-gate
fixture RED and exact-binding-guard mutation RED prove the test can distinguish
the boundary. Final local solutions passed `2237/2237`, including Transport
`702/702`; 80 fresh Debug processes passed the focused fact. CI
`33261748925` attempt 1 retained a macOS exit-137 format failure before tests,
while its unchanged-SHA attempt 2 passed `2237/2237` on Ubuntu, Windows, and
macOS plus Secret Scan and every unsigned package job. CodeQL `33261748927`
reported 52 rules, 0 results, and 0 open alerts. These are managed contract and
hosted scan/package results only. Exact artifacts and digests are recorded in
the Transport candidate evidence.

Documentation SHA `124b1a0c8325d7b469702682f8b7f14c1aebfa54` exposed a
macOS renderer-rejection test race after initiator acknowledgement verification
but before responder host-directory attachment publication. Immediate fixture
cleanup closed the still-borrowed stream. A temporary 100-ms delay made the same
failure deterministic. Test-only commit
`5e5f380393a46021d8106a7f3fa817d3b7ac3765` changes no production source:
the affected fixtures first assert the bounded Rejected result, then wait with a
cancellable five-second responder-publication barrier before cleanup. The probe
passed after that change and was removed. Local solutions passed `2237/2237`;
exact-SHA CI `33263840825`, CodeQL `33263840823`, Secret Scan, and all unsigned
package jobs passed. This is test synchronization, not a new security control or
a production acknowledgement-to-publication guarantee.

Test-only commit `8841080d8cfbfa3714b3cb7c6d858396ceb756b8` adds one T13
managed intersection and no production source change. After real protocol 1.7,
`FSM1`, Admission, encrypted media, and render, authenticated-control disconnect
starts cleanup. Emergency Stop registration disposal first revokes its callback,
then throws one injected `IOException`. That same instance remains observable
through `TerminalFailure` and coordinator Dispose, while capture/input Emergency
Stop and every later renderer, protection, permission, media budget, directory,
route, handler, channel, connection, and current/retained control owner drains.
At the `8841080` checkpoint the tracer was `14/14`; local solutions passed
`2238/2238`, and exact-SHA CI
`33264566458` passed `2238/2238` on every hosted OS plus Secret Scan and all
three reproducible unsigned package jobs. CodeQL `33264566368` evaluated 52
rules with 0 results and 0 open alerts.

This closes one active authenticated-disconnect by one Emergency Stop
registration-disposal cleanup fault only. It is not physical or native Emergency
Stop evidence, and every other cleanup owner/combination remains open.

Test-only commit `6ff3fefaa667e23f309681fe5fe953ae97bb5861` adds one full
T13 Desktop renderer-failure-to-replacement exact-binding trace without changing
production source. Generation 1
completes authenticated protocol 1.7 and `FSM1`, fails renderer Prepare, returns
Rejected before fail-close, and drains its published owner graph while the
accepted old listener handler remains gated before host-directory publication.
The same Device pair reconnects with strictly higher host and participant control
generations and fresh Session, Correlation, and Route IDs. Releasing only the old
gate yields the expected no-live-owner rejection while generation 2 remains
current, unstopped, host-unattached, and pre-Admission with no capture, send,
render, or retained driving/controller generation. Releasing generation 2's
independent gate then
allows Applied Admission and one BGRA-to-JPEG-to-encrypted-media-to-decode-and-
render transfer before Stop and complete owner drain.

A shared-gate fixture was RED after 442 ms, and removing the exact-binding
inequality guard made the focused test hit its 30-second bound; production was
immediately restored. Release-class validation exposed the media-attachment
handler's Completion-to-Exited publication gap, while fresh-process pressure
exposed participant renderer-disposal publication lag. Explicit bounded barriers
close both test-observation races. Final local evidence is `1/1` focused Debug
and Release, `15/15` tracer
Debug and Release, `160/160` fresh alternating processes, Desktop `550/550`, and
both complete solutions `2239/2239`, with clean warning-as-error builds, format,
diff, dependency-vulnerability, TEST MODE composition, and simulator gates.
Exact-SHA CI `33266348260` passed `2239/2239` on each hosted OS plus Secret Scan
and all three reproducible unsigned package jobs; CodeQL `33266348243` evaluated
52 rules with 0 results and the exact-commit branch query returned 0 open alerts.
Exact artifact IDs and digests are recorded in the managed tracer evidence.

This closes one managed causal trace, not the remaining T13 replacement/fault
matrix, non-cooperative native teardown, packaged churn, or resource telemetry.

Test-only commit `13681fb451df53290496416d11837ffb5435e500` adds the
sixteenth managed tracer case and no production source change. After real
protocol 1.7, `FSM1`, Admission, encrypted media, and render, participant control
disconnect invokes capture Emergency Stop. The test boundary clears current
capture ownership and throws one `IOException`; production exposes only the
stable unconfirmed-stop result containing `capture=local_boundary_exception`,
confirmed input and session reasons, no inner exception, and no raw injected
message. Later cleanup executes ordinary capture/input Stop, drains renderer,
protection, permission, media budget, both media directories/routes, handlers,
channels, connections, and current/retained control ownership. `TerminalFailure`
and the first explicitly observed coordinator `DisposeAsync` share one projected
failure instance.

The no-injection RED passed the historical row and timed out only the new row
after 20 seconds. The first one-shot throw demonstrated the existing bounded
projection, so the final assertions freeze the public T10 contract rather than
raw injected capture-boundary identity. Focused Debug/Release passed `2/2`, fresh-
process pressure
passed `160/160`, the tracer passed `16/16`, Desktop passed `551/551`, and both
solutions passed `2240/2240`; all local gates and strict P0/P1/P2 review passed.
Exact-SHA CI `33267557804` passed every hosted OS at `2240/2240`, Secret Scan,
and all unsigned package jobs. CodeQL `33267557806` passed 52 rules with 0
results and 0 exact-commit open alerts. Artifact details are in the managed
tracer evidence.

At the `13681fb` checkpoint, this closed the registration-disposal and capture-
Emergency cleanup-owner rows for one active disconnect. Every other cleanup owner
and combined-failure case remained open.

Test-only commit `2c6ff3221c494cd7003ad0a55e91c28e473615da` adds the
seventeenth managed tracer case without changing production source. Its third
disconnect theory row injects both prior managed faults at once. The final
terminal aggregate has exactly two direct inners in causal order: the bounded
capture result with no capture canary, followed by the exact registration
`IOException` instance. A test-side bounded wait avoids sampling an earlier
one-failure snapshot, and the first explicitly observed coordinator
`DisposeAsync` throws the same final outer aggregate instance. Exact
Emergency/ordinary Stop,
registration-disposal, connection, and owner-drain assertions all remain
satisfied.

The RED intentionally excluded the combined value from only the registration
injection predicate: the old rows passed and the combined row alone timed out at
20 seconds. The single predicate extension was GREEN. Focused Debug/Release
passed `3/3`, fresh-process pressure passed `240/240`, the tracer passed `17/17`,
Desktop passed `552/552`, and both solutions passed `2241/2241`; all local gates
and strict P0/P1/P2 review passed. Exact-SHA CI `33269125217` passed every hosted
OS at `2241/2241`, Secret Scan, and all unsigned package jobs. CodeQL
`33269125313` passed 52 rules with 0 results and 0 exact-commit open alerts.
Artifact details are in the managed tracer evidence.

At the `2c6ff32` checkpoint this closed one capture-plus-registration combined-
failure intersection. Every other cleanup owner, combination, and per-boundary
fault remained open.

Test-only commit `26cd380091f6fd387173e2565023cbb27a96aab0` adds the
eighteenth input Emergency Stop and nineteenth host-connection disposal rows
without changing production source. The input row records exactly one applied-
before-failure event and exposes only the bounded, canary-free input projection.
The connection row injects only after real inner disposal and preserves the
exact `IOException` instance through terminal observation. Both rows retain
exact Emergency/ordinary Stop, fail-close/disposal, and full owner-drain counts.

Separate TDD cycles first produced `3/4` and then `4/5`, with only each new row
reaching its 20-second bound while its injection was deliberately absent.
Enabling the two one-shot seams produced focused Debug/Release `5/5`; 40 fresh
alternating processes passed `200/200`; the tracer passed `19/19`; Desktop passed
`554/554`; and both complete solutions passed `2243/2243`. Warning-as-error and
all other local gates passed after strict review closed one P2 fixture-proof gap.
Exact-SHA CI `33270854982` passed `2243/2243` on each hosted OS, Secret Scan, and
all unsigned package jobs. CodeQL `33270854935` passed 52 rules with 0 results
and 0 exact-commit open alerts. Exact artifacts are recorded in the managed
tracer evidence.

This closes one input cleanup-owner row and one late authenticated-connection
disposal row. Every other cleanup owner, cross-product, and per-boundary fault
remains open.

Test-only commit `5c50870ee11639ee642781e647b135fdd4fc59f7` adds the twentieth
host fail-close row and twenty-first registration-plus-connection-disposal row
without changing production source. The fail-close row proves terminal shutdown
and CleanupCore share one after-inner failure Task: fail-close executes and fails
once, the exact `IOException` reaches `TerminalFailure` and the first explicitly
observed coordinator `DisposeAsync`, and later connection disposal plus every
owner drain still complete. The combined row produces one direct, flat
`AggregateException` whose two inners are exactly registration then connection-
disposal failure; both arise in the same cleanup result, so there is no partial
terminal publication to sample.

Separate TDD cycles produced `5/6` with only the new fail-close row reaching its
20-second bound, then a fast `6/7` exact-type failure when the combination still
injected only registration disposal. The two one-seam GREENs produced focused
Debug/Release `7/7`; 40 fresh alternating processes passed `280/280`; the tracer
passed `21/21`; Desktop passed `556/556`; and both complete solutions passed
`2245/2245`. Warning-as-error and all local gates passed; strict review reported
no P0/P1/P2 finding. Exact-SHA CI `33271787570` passed `2245/2245` on each
hosted OS, Secret Scan, and all unsigned package jobs. CodeQL `33271787616`
passed 52 rules with 0 results and 0 exact-commit open alerts. Exact artifacts
are recorded in the managed tracer evidence.

This closes one host fail-close owner and one registration-plus-connection-
disposal cross-product. Every other cleanup owner, cross-product, and per-
boundary fault remains open.

The remaining complete per-boundary reject/throw/cancel/timeout/revoke/
disconnect/cleanup-fault matrix remains open. Tasks 5, 5.5a, and 5.5, all native
and physical-device evidence, packaged accessibility, signed/notarized release
gates, and the long-running Goal also remain open. Hosted Windows and Linux
execution is managed contract evidence only; it does not establish real
operating-system permission revocation or native Windows/Linux capture, input,
or protection behavior, and no v1 release criterion is closed by this slice
alone.
`CreateProduction()` must continue to report Remote Window unavailable.

### 5.16 2026-08-30 pre-Prepare safety and macOS permission candidate

The H0/H1 rows in the finite
[`Remote Window production-boundary matrix`](../testing/remote-window-production-boundary-matrix.md)
now name this candidate boundary precisely. Before route selection or Prepare,
the source host must observe fresh exact-source `Safe` protection, revalidate
source/connection/permission/grant facts, observe a pure Emergency Stop readiness
fact, revalidate the host facts again, then cross caller-cancellation and
canonical-deadline barriers. A negative or changed fact keeps route, Prepare,
capture, controller, participant, Driver, input, rendering, and final Admission
authority closed.

For T10, non-fatal permission, authenticated-connection, protection, and
readiness exceptions reduce to `native_permission_unavailable`,
`authenticated_connection_stale`, `native_protection_not_safe`, and
`emergency_stop_readiness_unavailable`. Injected exception text and inner
exceptions must not cross the public failure surface; `OutOfMemoryException`
remains a fatal runtime condition. Cleanup joins any fail-close already started
by a pre-route safety callback, so connection disposal cannot race ahead and a
fail-close failure cannot escape terminal-failure accounting.

This narrows but does not eliminate T06/T07/T13 races. Synchronous callbacks do
not prove absolute TOCTOU linearization against an arbitrary concurrent thread.
The current readiness operation also does not reserve the eventual Emergency
Stop registration, so an atomic reservation or equivalent generation-bound
ownership proof remains a Task 5.5a blocker.

The macOS candidate maps CoreGraphics screen-capture preflight and explicit
request into bounded permission facts. Prompt-free snapshot reads never call the
request API, input remains `Unsupported`, and the adapter is not wired into
`CreateProduction()`. Late concurrent observations cannot overwrite newer
facts, one throwing observer cannot block later safety observers, and disposal
rejects late publication or new native calls. It proves no ScreenCaptureKit
capture, CoreGraphics input,
secure-input/protected-surface detection, persistent native Emergency Stop,
physical peer, packaged TCC, signing, or notarization claim.

Local Debug/Release solution verification passes `2286/2286` tests with zero
build warnings/errors. Exact-SHA CI `33275235290` and CodeQL `33275235305` pass
on evidence commit `92edfff`. Scope and verification details are in
[`2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md`](../evidence/2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md).
Tasks 5, 5.5a, 5.5, every native/physical/release gate, and the Goal remain open.

### 5.17 2026-08-30 participant policy and final Admission faults

For T10, a participant receive-policy reason is not trusted protocol text. Only
`renderer_unavailable` and `role_unsupported` cross that boundary; unknown text
and non-fatal policy exceptions reduce to `renderer_unavailable` before any
connection or renderer owner exists.

Final Admission publication similarly exposes only
`host_admission_publish_failed` for unexpected or foreign-token failures. Exact
caller cancellation remains distinguishable only after the production lease
maps its linked token back to the original caller token. A foreign-token OCE
cannot become caller cancellation merely because cancellation races it.

For T06/T13, the production-composed side-effect-then-throw row waits until the
participant endpoint has committed the known binding, then fails the host-side
publication wrapper. The host frame gate never opens, no media or render occurs,
fail-close and connection disposal execute once, and the directly asserted
owners across both nodes drain. This does not cover participant-endpoint throw,
authority revoke, authenticated disconnect, or every AD/HC cleanup variant.

Local Debug/Release solutions pass `2295/2295` with zero build warnings/errors;
format, vulnerability, explicit composition, simulator, diff, and final strict
review pass. Exact-SHA CI `33277518618` and CodeQL `33277518619` pass on evidence
commit `158c9a1`; downloaded artifacts prove `2295/2295` on each hosted OS,
Gitleaks 208/0, and CodeQL 52/0. Task 5.5a and all native, physical,
signing/notarization, and release gates remain open.

### 5.18 2026-08-30 Host Preparation reservation core

Commit `294042fdfcc346e3eade3551d57cc7ccba95c601` implements only the
internal Desktop reservation core proposed by
[ADR 0027](../adr/0027-remote-window-host-preparation-reservation.md). Its six
independent opaque epochs represent Source, Permission, Authorization,
Connection, Emergency Stop readiness, and Protection. One bundle can bind one
host generation only, so bundle reuse, stale generation, stale epoch, and
regrant ABA cannot revive or invalidate a replacement reservation.

For T06/T07, the core defines the linear alternatives `M < R`, `R < M < S`, and
`S < M`; route admission becomes the conservative connection-consumption point,
and no phase can skip from Collecting to route work or from Prepare to promotion.
Deadline equality fails closed at Arm, route admission, Prepare send admission,
Ready matching, and promotion. For T10, fact invalidation accepts no free-form
reason: it derives one fixed payload-free reason for each of the six facts, and
foreign Ready uses `remote_window_ready_mismatch`. A late canary cannot be
validated, thrown, or reflected after another terminal transition. For T13,
six concurrent invalidations share one terminal completion, and an epoch bundle
cannot be claimed twice.

TDD first exposed the missing core, incomplete deadline terminal, foreign Ready
without terminal fail-close, bundle reuse, late canary throw/leak, and missing
Collecting phase. Strict review initially returned BLOCK with one P1 and two P2
findings. The single-claim, explicit Arm, complete deadline, fixed-reason, and
late-terminal repairs received final APPROVE with 0 P0, 0 P1, and 0 P2 findings.
Local focused Debug/Release passed `9/9`, Desktop Debug/Release `590/590`, and
solution Debug/Release `2304/2304`; warning-as-error build, format, diff,
vulnerability, explicit composition, and simulator gates passed. The
[core evidence](../evidence/2026-08-30-host-preparation-reservation-core.md)
records exact commands and limitations. No workflow ran at bare implementation
SHA `294042f`; hosted evidence commit `fa70e63` contains the core and passes
`2304/2304` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and the
reproducible unsigned packages.

At that isolated-core commit this was not yet a T06/T07 production mitigation:
the coordinator and fact owners did not use the core. The subsequent source-
only production composition is recorded below. At that later checkpoint,
Permission, Trust/Capability, authenticated Connection mutation, Emergency Stop
reserve/promote, and Protection did not implement the complete reservation
contract, so no aggregate matrix cell closed.

### 5.19 2026-08-30 Host Preparation source linearization

Exact commit `ec63942296175f63964d8f463335d6b621e22042` composes one Desktop
host Preparation reservation across the exact Platform source invalidation
slot, generation-bound authenticated responder-route operation, actual
Transport Prepare send-admission hook, Ready match, post-Ready revalidation,
promotion, and coordinator cleanup.

For T06/T07, the production-composed managed tracer proves Source
`R < M < S`. After a real authenticated protocol-1.7 route is selected, exact
source unregister invalidates the reservation under the source-state mutation
gate. The later real send-admission hook rejects and reports NotDelivered; no
Prepare wire, participant policy, media attachment wait, capture, media send,
renderer preparation, render, or final Admission occurs. The old source cannot
be reacquired. The existing two-node success tracer traverses the same
reservation through Ready and promotion before capture, Driver input, and
Emergency Stop.

For T10, source terminal state exposes only `native_source_stale`; concurrent
route or wire exception text cannot replace it. An exact caller cancellation
retains its original exception and token instead of being relabelled as a source
failure. For T13, route admission conservatively owns connection cleanup.
Fail-close and connection disposal run once, and the coordinator, controller,
control handlers, media directories, routes, leases, channel, observers, and
budgets drain without resurrection. Source invalidation is a bounded state
transition and performs no external cleanup while holding the reservation gate.

Focused host Debug/Release passes `44/44`, the new production tracer `1/1`,
Desktop `596/596`, and both solutions `2334/2334`; warning-as-error build,
format, diff, dependency vulnerability, explicit composition, simulator, and
final strict review pass. Exact-SHA CI `33281547016` and CodeQL `33281546949`
pass; downloaded artifacts prove `2334/2334` on every hosted OS, Gitleaks
208/0, CodeQL 52/0, and all reproducible unsigned packages. Exact jobs,
artifacts, digests, commands, and limitations are recorded in the
[source-linearization evidence](../evidence/2026-08-30-host-preparation-source-linearization.md).

This evidence proves neither production-composed Source `M < R` nor `S < M`.
At that checkpoint, Permission revision, exact Trust/Capability mutation,
authenticated Connection fact invalidation at its mutation gate, Emergency
Stop readiness reserve/promote, exact Protection epochs, and the complete per-
boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix
remained open. Hosted managed execution does not prove native source lifetime
or protection behavior on any operating system.

### 5.20 2026-08-30 Host Emergency Stop readiness reservation

Exact commit `8e349cc7d9f722caa7e6df404ec6a59117d7d588` composes the
Emergency Stop fact through a managed process-local registrar reservation. For
T06/T13, one slot binds the exact host owner and Session generations before
route admission but installs no activation callback. Registrar readiness loss
and one-time promotion linearize under the same gate, so a concurrent loss
reaches exactly one of the pre-Ready Preparation invalidation sink or the
post-promotion formal registration-loss callback. Release makes stale promotion
fail and cannot invalidate an ABA replacement. Sink failure releases the slot;
registrar disposal retains one exact failure without repeating invalidation.

The coordinator promotes the same owner only after Ready, media attachment,
host-fact revalidation, and a fresh formal protection observation. Promotion
rejection, unexpected throw, side-effect-then-throw, activation/disposal during
promotion, and exact caller cancellation remain pre-capture and terminal. A
formal owner produced before failure is retained until the controller's native
capture, input, and sharing stops have been attempted, preventing a cleanup gap
that could release the local Emergency Stop before authority is removed.

For T10, readiness loss exposes only
`emergency_stop_readiness_unavailable`; unexpected promotion throws expose only
`emergency_stop_registration_failed`. Injected registrar text and inner
exceptions do not cross the product failure surface. The production-composed
managed tracer proves Emergency Stop `R < M < S` over real authenticated
loopback route and actual Transport send admission: readiness loss consumes the
owned connection, the later send hook admits no Prepare wire, no capture/media/
render/Admission follows, and both nodes' directly asserted owners drain.

Platform and Desktop Debug/Release pass `239/239` and `608/608`; both solutions
pass `2355/2355` with zero build warnings/errors, and every local gate passes.
Exact-SHA CI `33283264188` and CodeQL `33283264254` pass; retained artifacts
prove `2355/2355` on Windows, Linux, and macOS, Gitleaks 208/0, CodeQL 52/0,
and all reproducible unsigned packages. Final strict review returned APPROVE
with 0 P0/P1/P2 after two initial P1 findings and one later P1 finding were
repaired. Exact jobs, artifacts, digests, commands, and limitations are in the
[Emergency Stop readiness evidence](../evidence/2026-08-30-host-emergency-stop-readiness-reservation.md).

This is not a native hotkey or operating-system Emergency Stop result. At that
checkpoint the other Emergency Stop `M/R/S` orders, its complete fault matrix,
real Windows/macOS/Linux registration and loss, physical latency, blocked UI/
network, secure-input behavior, Source `M < R` and `S < M`, Permission, Trust/
Capability, authenticated Connection mutation, Protection, and the complete
production-boundary matrix remained open.

### 5.21 2026-08-30 Host Trust and Capability Preparation reservation

Exact commit `635dc23ec0c8f2812d527e16135b3d9c40885788` mitigates one
pre-Prepare T05/T06/T13 Authorization race. The authenticated connection lease
retains the peer public-key fingerprint proved by its real handshake. The
Security coordinator reserves the exact Device ID, that fingerprint, and all
Capabilities required by the frozen role under the same gate used to commit
Trust revocation or Capability update. ViewOnly requires `mirror.view`;
DriverEligible requires both `mirror.view` and `mirror.drive`.

For T05, every Applied revoke or Capability update invalidates matching
Preparation registrations after the store commit and before `Changed` observers
or active-session Stop. An Applied update with the same visible grant still
invalidates: value equality cannot revive an operation reservation across an
authoritative mutation. A rejected, thrown, or caller-cancelled store operation
does not invalidate because it did not commit. Exact fingerprint matching,
monotonic registration identity, and late-old-dispose tests prevent same-Device
key replacement and revoke/regrant ABA from retargeting an old reservation.

For T10, missing or changed authenticated identity maps only to
`authenticated_connection_stale`; absent Trust or required Capability maps only
to `mirror_capability_denied`; and an unexpected non-fatal reservation failure
maps only to `mirror_authorization_unavailable`. Injected text and inner
exceptions do not cross the host failure surface. Exact caller cancellation
retains its token. Fatal `OutOfMemoryException` from reservation, invalidation,
or active-session Stop escapes unwrapped and by identity rather than being
misreported as denial or unavailability.

For T06/T13, matching registrations are all deactivated under the mutation gate
before their bounded invalidation sinks run. Non-fatal sink failures do not undo
the committed mutation or skip active-session Stop; failure identity and order
remain observable. The Desktop coordinator reserves before route, checks the
same registration before promotion, releases it after promotion, and owns it
through terminal cleanup. Focused tests cover `M < R`, `R < M < S`, and
`S < M`, plus rejection, throw, cancellation, fatal failure, and selected
normal/cancellation release outcomes. Authorization-registration release-fault
intersections remain open.

The production-composed managed tracer proves only Authorization
`R < M < S`. After a real authenticated protocol-1.7 route is selected, an
Applied same-grant update invalidates the exact reservation while source and
connection remain current. The actual Transport send-admission hook admits no
Prepare wire; no participant policy, attachment wait, capture, media, renderer,
Admission, or input follows; and both managed owner graphs drain. Unit order
shapes are not evidence for the missing production-composed orders.

Local Debug/Release solutions pass `2377/2377` with zero build warnings/errors;
format, dependency vulnerability, explicit TEST MODE composition, simulator,
diff, and final strict reviews pass. Exact-SHA CI `33284857461` and CodeQL
`33284857449` pass; retained artifacts prove `2377/2377` on Windows, Linux, and
macOS, Gitleaks 208/0, CodeQL 52/0, and three reproducible version-0.1.195
unsigned packages. Exact jobs, artifacts, digests, commands, and limits are in
the [Trust/Capability Preparation evidence](../evidence/2026-08-30-host-trust-capability-preparation-reservation.md).

This is hosted managed, test-mode, and unsigned-package evidence. It proves no
native API, physical two-Device path, signed package, notarization, packaged
accessibility, or release acceptance. The other Authorization orders and fault
intersections, Permission, authenticated Connection mutation, Protection,
remaining Source and Emergency Stop orders, and the complete boundary matrix
remain open. H0/H1 stay P or M; Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the Goal remain open.

### 5.22 2026-08-30 Host Permission Preparation reservation

Exact commit `d607ed1c3217c9c4102c4b893d20da9a6845f02d` mitigates one
pre-Prepare T06/T07/T13 Permission race. A synchronous prompt-free reservation
binds the exact permission owner generation, revision, capture/input facts, and
frozen role under the permission boundary's accepted-observation gate.
ViewOnly requires Granted capture; DriverEligible additionally requires Granted
input. A stale snapshot or required-role denial makes the host reservation
terminal before route or the next send admission, while an unavailable,
unsupported, disposed, or absent reservation boundary fails closed.

For T06/T07, the macOS boundary assigns an operation sequence before each
CoreGraphics preflight or request and commits only non-stale observations. A
changed permission fact advances the revision, deactivates all current
Preparation registrations, and invokes their bounded invalidation sinks before
ordinary `Changed` observers. Repeating the same fact preserves the revision
and reservation. Exact revision and registration identities prevent
Revoked/Granted or late-old-dispose ABA from reviving or removing a replacement.
This orders accepted Flowspan observations; it cannot lock or claim an external
TCC transition before a later prompt-free preflight observes it.

For T10, snapshot drift and required-role denial expose only
`native_permission_denied`; unsupported, unavailable, missing-contract, and
unexpected non-fatal failures expose only `native_permission_unavailable`.
Injected exception and interop text does not cross the host failure surface.
Exact caller cancellation retains its exception and token; foreign cancellation
is not relabelled; and `OutOfMemoryException` escapes unchanged.

For T13, the invalidation sink synchronously receives registration ownership
before the reservation operation can later throw. All registrations become
inactive before sink delivery, so a non-fatal sink failure cannot preserve the
old fact, block later invalidations or ordinary observers, or undo the commit.
Multiple failures retain registration order; fatal exhaustion remains raw;
repeat disposal rethrows the same retained failure without repeating
invalidation. The coordinator rechecks the exact registration before promotion,
releases it after promotion, and owns it through terminal cleanup.

The production-composed managed tracer proves only Permission `R < M < S`.
After a real authenticated protocol-1.7 responder route is selected, a managed
Granted-to-Revoked revision invalidates the exact reservation; actual Transport
send admission emits no Prepare wire or later authority; regrant does not revive
the terminal generation; and both managed owner graphs drain. The production-
composed row uses a managed permission boundary, not the macOS CoreGraphics
boundary.

Local Platform, macOS Platform, Desktop, and solution Debug/Release pass
`240/240`, `64/64`, `639/639`, and `2418/2418`; warning-as-error builds have
zero warnings/errors, all local gates pass, and final review reports no P0/P1
finding. Exact-SHA CI `33286525528` and CodeQL `33286525529` pass; artifacts
prove `2418/2418` on every hosted OS, Gitleaks 208/0, CodeQL 52/0 with no open
alerts, and three verified version-0.1.196 reproducible unsigned packages.
Exact jobs, artifacts, SARIF/package digests, commands, and limitations are in
the [Permission Preparation evidence](../evidence/2026-08-30-host-permission-preparation-reservation.md).

This does not prove real macOS TCC revocation or recovery, Accessibility/input,
Windows or Linux native permission handling, physical two-Device behavior,
signed packaging, notarization, or release acceptance. Production-composed
Permission `M < R`, `S < M`, and the remaining fault intersections;
authenticated Connection mutation; Protection; other fact orders; and the
complete matrix remain open. H0/H1 remain P or M; Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/release gate, and the Goal remain
open.

### 5.23 2026-08-30 Host authenticated Connection Preparation reservation

Exact commit `259c3bbda4648bc6c45b71d78fbc7a34feb4de71` mitigates one
pre-Prepare T06/T10/T13 Connection race. A synchronous composite registration
binds the exact `RemoteWindowConnectionGeneration` and its exact
`AuthenticatedRemoteWindowMediaSession` under both authoritative gates. A
generation point read can no longer stand in for media currentness: the
registration remains current only while it is active and is the same exact
object in both slots.

For T06, generation revoke/owner release, explicit or committed deferred
fail-close, media Dispose, first control-stop commit, and responder-route
invalidation all deactivate the temporary registration under their mutation
gate. Generation invalidation precedes ordinary revocation callbacks; media
invalidation precedes public control-stop signalling. Route selection and
actual Prepare send admission require the same exact registration while it is
active, so a public, foreign, stale, omitted, or other-lease owner cannot cross
those gates. The Desktop coordinator rechecks the exact registration before
host-reservation promotion and releases it only after promotion.

The ordinary live-session revocation owner is now a composite registration over
generation and media control-stop with one exact-once invocation. It is
installed while the temporary registration is still current. A media mutation
during the promotion/release overlap therefore invokes Emergency Stop before
capture even when the generation has not yet been revoked. Partial live-
registration setup rolls back both handles, concurrent generation/media causes
cannot double-invoke, and late old disposal cannot remove a replacement owner.

For T10, reservation conflict, stale generation/media, unexpected non-fatal
reservation failure, and initial or promotion currentness failure expose only
`authenticated_connection_stale`. Injected exception and cleanup text does not
cross the host start surface. Exact caller cancellation retains its token;
foreign cancellation is treated as an unexpected failure; and fatal
`OutOfMemoryException` escapes by exact instance rather than being relabelled.

For T13, the sink synchronously claims cleanup ownership before the reservation
returns. A failed claim rolls back both slots and deactivates the registration.
Monotonic registration IDs plus exact-object release prevent ABA. Non-fatal
invalidation, fail-close, timer, and registration-cleanup failures retain order
while all later cleanup is attempted; repeat fail-close/disposal shares or
replays the same terminal result as specified. Fatal exhaustion remains raw
after applicable owner cleanup.

Focused coordinator tests cover `M < R`, `R < M < S`, and `S < M`, along with
conflict, claim-then-throw/cancel, currentness failures, redaction, raw fatal
failure, release, and cleanup. Transport tests cover exact two-slot ownership,
both route and actual send gates, generation/media invalidation, live exact-
once callback setup/teardown, failure ordering, and ABA.

The managed production-composed disconnect tracer selects a real authenticated
protocol-1.7 responder route, then loses authenticated control. Fact Connection
becomes terminal with reason `authenticated_connection_stale`, no later
authority opens, and both nodes drain. That disconnect prevents execution from
entering the actual Prepare send-admission hook, so its zero admission count is
not claimed as send-gate rejection; the real `RemoteWindowControlSession`
two-lease Transport regression supplies that evidence. A second managed tracer
reaches Ready and verified bilateral `FSM1` attachment, commits media control
stop in the promotion/release overlap, and proves the live callback and
Emergency Stop precede capture with complete drain.

Local Transport, focused media-session, Desktop, and solution Debug/Release
pass `755/755`, `41/41`, `654/654`, and `2469/2469`; warning-as-error builds have
zero warnings/errors and all local gates pass. Two independent final reviews
report no P0/P1 finding. Exact-SHA CI run `33289550263` has successful Windows,
macOS, Linux, and Secret Scan jobs: retained artifacts prove `2469/2469` on each
hosted OS and Gitleaks 208/0. CodeQL run `33289550265` passes 52/0 with 0
exact-ref open alerts. All three reproducible version-0.1.197 unsigned packages
pass their checksum, repository verifier, exact metadata, archive, manifest,
and canonical-tree checks. Exact jobs, artifacts, digests, commands, and
limitations are in the
[Connection Preparation evidence](../evidence/2026-08-30-host-connection-preparation-reservation.md).

This is managed same-host loopback, hosted contract, and static-analysis
evidence. It proves no native API, physical two-device operation, packaged
Windows/macOS/Linux behavior, signing, notarization, or release acceptance. At
that checkpoint the remaining production-composed Connection orders and fault
intersections, Protection reservation, other fact orders, and the complete
matrix remained open. H0/H1 remained P or M; Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/release gate, and the Goal remained
open.

### 5.24 2026-08-30 Host native Protection Preparation reservation

Exact commit `c987ca84e1f9f867f0edef3222a94dc8d25a2583` mitigates one
pre-Prepare and capture-start T06/T07/T10/T13 Protection race without enabling
the shipped production composition.

Exact evidence/test-stabilization commit
`457a2c4b9e3d6905218e826cedd60029bbd1b35e` preserves that production
implementation and makes the formal source-loss test wait for deterministic
asynchronous terminal cleanup before asserting the final coordinator snapshot.

For T07, one synchronous registration binds the full accepted protection
observation: owner, session, and source generations; revision; kind;
`ObservedAt`; and source identifier. Reservation requires exact equality and a
fresh `Safe` value under the source mutation gate, transfers host ownership
before returning, and rolls back a failed transfer. Its monotonic ID and exact-
object slot release prevent a late old Dispose from removing a replacement.
Temporary mutation or source loss deactivates the exact owner under that gate
before ordinary observers, and a later Safe value cannot revive it.

The host also binds the observation's inclusive validity interval from
`ObservedAt - MaximumFutureClockSkew` through
`ObservedAt + MaximumProtectionAge`. Arm, route admission, actual Prepare send
admission, Ready matching, and host promotion recheck it; request-deadline
equality remains expired and wins classification. The protection boundary
independently rechecks exact identity and freshness when the same registration
moves from `Temporary` to `FormalPreStart`, and again with a fresh post-
`Starting` clock immediately before source use/native capture. Mutation that
wins the source gate prevents capture; mutation after admission observes a
controller already in `Starting` and closes the formal live gates.

For T06, live latch first closes controller Protection admission under the
source mutation gate, then retains the exact observation and admission epoch in
a bounded FIFO. Notify runs outside that gate and before ordinary observers.
Every native frame destination and native or semantic input boundary holds one
exact `ProtectionAdmissionUse` for the complete local call. New uses fail while
closed, and even a current Safe reconciliation cannot reopen until older uses
drain and the same epoch, accepted observation, Active lifecycle, and Capturing
state still match. Active callback ancestry avoids self-wait without granting a
new use.

For T13, non-reentrant formal Notify callers wait until the sequence they
observed has drained. Active ancestry leaves queued work to the active outer
drainer, preventing self- and cross-boundary deadlock; stale captured contexts
cannot bypass a current drainer. Reversed concurrent notifications preserve
unsafe-before-Safe order. Source loss is terminal, queue pressure fails closed,
external source Dispose joins in-flight formal work, and applicable callback and
cleanup failures remain observable. Nested or direct fatal exhaustion escapes
as raw `OutOfMemoryException` after applicable cleanup.

For T10, reservation conflict, changed/unsafe/stale observation, non-fatal
reserve/promotion/currentness failure, and capture-start denial expose only
`native_protection_not_safe`. Exact caller cancellation retains its token and
fatal exhaustion is not relabelled. No raw probe source, callback exception, or
injected canary crosses the host start reason.

On implementation tree `c987ca8`, Platform and Desktop Debug/Release pass
`289/289` and `700/700`; both solution configurations pass `2564/2564` with zero
warnings/errors; the managed success plus `SecureInput`/`Unknown` tracer passes
`3/3`; and every stated local gate passes. Two independent final reviews report
no P0/P1 finding. On test-only tree `457a2c4`, the focused formal-source-loss
Release row, 50 repeated local executions, and both full `2564/2564` solution
configurations pass. Exact-SHA CI `33294103546` and CodeQL `33294103609` pass;
downloaded artifacts prove `2564/2564` on every hosted OS, Gitleaks 208/0,
CodeQL 52/0 with 0 exact-ref open alerts, and all three reproducible
version-0.1.200 unsigned packages. Exact commands, jobs, artifacts, digests,
and limitations are in the
[Protection Preparation evidence](../evidence/2026-08-30-host-protection-preparation-reservation.md).

The two negative managed tracer executions use authenticated protocol-1.7
loopback and the actual Transport send-admission hook, but prove only Protection
`R < M < S`. The hook returns `NotDelivered`, no Prepare wire or later authority
opens, and both nodes drain. This is not native secure-input/protected-content,
physical Windows/macOS/Linux, or production-composed `M < R`/`S < M` evidence.
The complete per-boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-
fault matrix remains open. H0/H1 remain P or M; Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the Goal remain open.

### 5.25 2026-08-30 Pending renderer authenticated disconnect

Exact commit `8d0831d0716bc68bc1d5dc0ff18c4efc033624b7` adds one managed
TX/P0/P2/CL tracer after Prepare send admission and bilateral verified `FSM1`
attachment. The participant renderer factory is inside a deliberately non-
cooperative Preparation call and no Ready outcome has been produced when the
authenticated control connection disconnects.

The participant's owned disconnect cleanup enters, cancels the Preparation
lifetime, and waits. Cancellation is observed, but disconnect, the worker, and
renderer preparation remain incomplete while the renderer retains its call.
This is required fail-closed ownership behavior, not a cleanup hang assertion:
production does not claim to preempt arbitrary non-cooperative code. In
parallel, the host reservation becomes terminal for Connection loss with
`authenticated_connection_stale` and connection-consuming cleanup.

After explicit release, the participant returns exactly one local terminal
`Rejected/preparation_cancelled`, disposes the late renderer, and completes
disconnect cleanup. No host Ready acknowledgement, Admission, capture, media
send, render, or input authority appears. Host fail-close and connection Dispose
each run once and the complete asserted owner graph on both nodes drains.

The deliberate RED sentinel failed `0/1`; final focused Debug/Release pass
`1/1`, twenty fresh processes per configuration pass `20/20`, the full tracer
passes `31/31`, Desktop passes `701/701`, and both solution configurations pass
`2565/2565`. Warning-as-error builds have zero warnings/errors, and format plus
diff verification pass. Exact-SHA CI `33295825931` and CodeQL `33295825897`
pass; retained artifacts prove `2565/2565` on each hosted OS, Gitleaks 208/0,
CodeQL 52/0 with 0 exact-ref open alerts, and all three reproducible
version-0.1.202 unsigned packages. Exact jobs, artifacts, digests, commands, and
limitations are in the
[pending-renderer disconnect evidence](../evidence/2026-08-30-pending-renderer-authenticated-disconnect.md).

This changes only P0 Disconnect from M to P. TX/P2/CL remain P, every other
matrix cell is unchanged, and the remaining transaction phases, Trust/lease
revocation, renderer timeout, cleanup-fault combinations, and non-cooperative
native teardown remain open. This is managed same-host macOS evidence, not
native or physical Windows/macOS/Linux evidence. Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the Goal remain open.

### 5.26 2026-08-30 Pending renderer exact deadline

Timeout implementation commit `40d4f78f32bb9958c1e7fbc075b6743620d1f0de`
adds one managed production path across TX/P0/P2/CL. Final evidence tree
`de4009aae9b7e5822983e13e70909b7deb8c2b64` preserves that path and
hardens the exact shutdown classifier plus local-pairing publication lifetime.

After Prepare send admission and bilateral verified `FSM1` attachment, separate
manual clocks keep the host before deadline while advancing only the participant
to exact request-deadline equality. Peer disconnect has not entered when the
blocked renderer's lifetime token is cancelled. Before release there is no Ready
outcome or host Ready authority. Release produces exactly one bounded
`Rejected/preparation_expired`, then disconnect; only the documented bounded
host terminal tuples are accepted. No Admission, capture, media send, render, or
input occurs, and the late renderer plus both-node owner graph drain.

By fault-origin classification this advances only P2 Timeout from M to P. The
cleanup completes without a cleanup timeout, so CL Timeout remains M. Other
cells do not change.

Final local evidence is focused `2/2`, fresh deadline Debug/Release `10/10`
each, tracer `32/32`, Desktop `707/707`, solution `2571/2571`, zero build
warnings/errors, and passing format/diff checks. Three strict reviews report zero
P0/P1/P2 after the final repairs. Earlier CI `33296383742` at `c761acf` is not
successful evidence: Windows job `99216650548` exposed an exact stale-aggregate
classification gap and `Task.Run` publication starvation. Its CodeQL run
`33296383740` succeeded independently. CI `33297152942` and CodeQL
`33297152906` pass for timeout implementation tree `40d4f78`. Final exact-SHA
CI `33298564630` and CodeQL `33298564676` pass for `de4009a`; retained
artifacts prove `2571/2571` on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with
0 exact-ref open alerts, and all three reproducible version-0.1.205 unsigned
packages. Exact jobs, artifacts, digests, commands, run history, and limitations
are in the
[pending-renderer deadline evidence](../evidence/2026-08-30-pending-renderer-deadline.md).

This proves no native renderer, physical Device pair, Windows/macOS/Linux native
runtime, signing, notarization, or release acceptance. Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/release gate, and the Goal remain
open.

### 5.27 2026-08-30 Participant current-lease acquisition ownership

For T06 and T13, implementation commit
`681c0f72b4f584aba8fa6bf7e915a27317636ff9` closes one owner-transfer
window at participant P0. The authenticated current-connection collaborator may
write a real generation-bound `out` lease and then throw. The peer now attaches
every non-null lease to its generation in a `finally` around that call, so
exception classification cannot strand already-acquired authority outside the
terminal owner graph.

The non-fatal connected-peer test assigns the real lease and throws
`IOException`. The public outcome is bounded to
`Rejected/media_unavailable`; renderer Preparation remains zero; the
side-effect lease is no longer current; and the host's lease remains current.
A subsequent acquisition succeeds, while late/idempotent disposal of the old
lease cannot invalidate that replacement. The fatal companion assigns the same
kind of lease and throws one exact `OutOfMemoryException`; the identical fatal
object escapes, while the lease remains current until owned peer cleanup then
releases it. Thus neither bounded projection nor raw fatal propagation abandons
the completed authority side effect.

Final local tree `213327c4373379f5f92457ab651741dd5bdd85c4` passes the
focused rows `2/2`, twenty fresh processes per configuration and `80/80` total
test invocations, Desktop `709/709`, solution `2573/2573`, and the unchanged
managed tracer `32/32`, all in Debug and Release. Builds have zero warnings/
errors and format/diff checks pass. Exact hosted artifacts and limitations are
recorded in the
[participant current-lease ownership evidence](../evidence/2026-08-30-participant-lease-acquisition-ownership.md).
Final exact-SHA CI `33300966551` and CodeQL `33300966509` pass for `15aba95`;
artifacts prove `2575/2575` on each hosted OS, Gitleaks 208/0, CodeQL 52/0 with
0 exact-ref open alerts, and all three reproducible version-0.1.209 unsigned
packages.

P0 Throw was already Partial and remains Partial; this adds no tracer case and
changes no other matrix cell. Current Trust/lease revocation, other participant
disconnect phases, and remaining cleanup-fault intersections remain open. This
is managed same-host and portable contract evidence, not a native API, physical
two-Device, signed/notarized package, packaged accessibility, or release result.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every
native/physical/signing/notarization/release gate, and the Goal remain open.

### 5.28 2026-08-30 Pending renderer participant Trust revoke

For T05, T06, and T13, exact test-only commit
`8413d065ba7f9d2d2b05e8b52d9c97eace768cf9` adds one
production-composed participant Trust-revoke row with no production source
change. It runs a real
signed/verified candidate through `AuthenticatedTcpPeerSessionAttempt` and
`SystemAuthenticatedTcpConnector`, so the current authenticated session and its
generation-bound Remote Window lease are owned by the production Trust/reconnect
path.

After exact Prepare send admission and bilateral verified `FSM1`, renderer
Preparation is deliberately blocked with no Ready authority. Calling the real
`participantTrust.RevokePeerAsync` removes Trust, invalidates the lease, denies
reacquisition, enters authenticated disconnect cleanup, and cancels the renderer
lifetime. Before release, the Trust call and session attempt remain incomplete,
the renderer retains its call and is not disposed, peer cleanup has entered but
not completed, and Ready, Admission, capture, media send, render, and input all
remain closed. Thus authoritative revoke removes access immediately without
claiming premature owner drain.

Renderer release yields one bounded `Rejected/preparation_cancelled`, disposes
the late renderer, and allows Trust revoke to return `true`. The production
session attempt terminates as `PermanentRejection/PeerNotTrusted`; the host
reports bounded `authenticated_connection_stale`; and the asserted owner graph
drains on both nodes. Local focused Debug/Release pass `1/1`, ten fresh processes
per configuration pass `10/10`, the tracer passes `33/33`, and Desktop passes
`710/710`, all in both configurations. The solution passes `2574/2574` in both
configurations; warning-as-error builds have zero warnings/errors; and format/
diff checks pass. Immediate parent `d89758b` is a test-fixture-only starvation
repair, not production notification behavior. Final exact-SHA CI `33300966551`
and CodeQL `33300966509` pass for `15aba95`; retained artifacts prove
`2575/2575` on all hosted OSes, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref
open alerts, and three reproducible version-0.1.209 unsigned packages. Commands,
jobs, digests, and limitations are in the
[pending-renderer Trust-revoke evidence](../evidence/2026-08-30-pending-renderer-trust-revoke.md).

By fault origin only P0 Revoke and P2 Revoke change from M to P. TX Revoke and
CL Revoke gain direct owner-path evidence but remain P; every other cell is
unchanged. Other revoke phases, revoke-plus-cleanup failures, direct generation-
only revoke, remaining disconnect phases, and native non-cooperative teardown
remain open. This is managed same-host macOS evidence, not native API, physical
two-Device, packaged accessibility, signed/notarized package, or release proof.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every
native/physical/signing/notarization/release gate, and the Goal remain open.

### 5.29 2026-08-30 Final-Admission authority revoke

For T05, T06, and T14, exact test-only commit
`15aba95409c62d858669b740957da54a5bce6b95` adds one
production-composed authority revoke at the final-Admission side-effect window.
The participant has committed and published the exact `Applied` or
`AlreadyApplied` Admission state, but the host has not yet performed its post-
publication revalidation or opened `Admission.TryOpen()`.

At this point capture has started once and its initial pre-Admission frame is
disposed. A second frame emitted inside the boundary hook is also disposed
exactly once with zero media send/render; input remains empty and the
authenticated generation is current. Real `hostTrust.UpdateCapabilitiesAsync`
applies the exact fingerprint-bound grant change from `mirror.view` to empty.
The mutation removes current
Mirror authority and drains the matching inbound connection; when it returns,
the old generation is non-current and cannot be reacquired.

The host exposes only bounded `authenticated_connection_stale`, with no peer
fingerprint or dependency payload. Capture and input receive local Emergency
Stop, the final frame gate never opens despite participant Admission commit, and
the asserted owner graph drains on both nodes. The exact source remains current,
so this is not misclassified as a source-revoke result. The focused pair passes
`2/2`; ten fresh final-row processes pass `10/10` per configuration; tracer,
Desktop, and solution pass `34/34`, `711/711`, and `2575/2575`; builds have zero
warnings/errors; format/diff checks pass; and independent strict review reports
APPROVE with 0 P0/P1/P2 findings. These results hold in Debug and Release where
applicable. Final exact-SHA CI `33300966551` and CodeQL `33300966509` pass;
artifacts prove `2575/2575` on every hosted OS, Gitleaks 208/0, CodeQL 52/0 with
0 exact-ref open alerts, and all three reproducible version-0.1.209 unsigned
packages. Commands, jobs, digests, and limitations are in the
[final-Admission authority-revoke evidence](../evidence/2026-08-30-final-admission-authority-revoke.md).

By fault origin only AD Revoke advances from M to P. The path crosses HC and CL,
but does not directly inject an HC revoke or complete CL revoke combinations;
HC Revoke remains M and CL Revoke remains P. Other Admission revoke phases,
authenticated disconnect, participant-endpoint failure, revoke-plus-cleanup
faults, and native non-cooperative teardown remain open. This is managed same-
host evidence, not native API, physical two-Device, packaged accessibility,
signed/notarized package, or release proof. Tasks 5, 5.5a, and 5.5, aggregate
H0/H1 acceptance, `CreateProduction()`, every native/physical/signing/
notarization/release gate, and the Goal remain open.

### 5.30 2026-08-30 Final-Admission authenticated disconnect

For T03, T06, and T14, exact test-only commit
`7be177bb010c55ba44c852a851b60c3ba843d9d7` adds independent
authenticated transport loss at the final-Admission side-effect window. The
participant has committed and published exact `Applied` or `AlreadyApplied`,
but the host has not completed post-publication revalidation or opened the frame
gate.

Capture has started once, the initial pre-Admission frame is disposed, and a
second hook-emitted frame owner is disposed exactly once with zero media send/
render; input remains empty. The hook starts real participant connection
disposal, then waits for a barrier published after the production host
revocation callback returns without awaiting full teardown. At that point the
old generation is non-current and unreacquirable, while the Trust record, exact
fingerprint, and sole `mirror.view` grant remain unchanged.

Host Start exposes only `authenticated_connection_stale`, with no inner
exception, fingerprint, or dependency payload. Capture/input receive local
Emergency Stop and the frame gate remains closed. Host Start awaits its owned
cleanup before returning; the test then joins disconnect and session completion
outside the hook and verifies both nodes' owner graphs are drained.

Focused final-Admission Debug/Release pass `3/3`; ten fresh disconnect processes
per configuration pass `10/10`; tracer, Desktop, and solution pass `35/35`,
`712/712`, and `2576/2576`; builds have zero warnings/errors; format/diff checks
pass; and self plus independent review report 0 P0/P1/P2 findings. Exact commands
and limitations are in the
[final-Admission disconnect evidence](../evidence/2026-08-30-final-admission-authenticated-disconnect.md).

Final exact-SHA CI `33302056214` and CodeQL `33302056182` pass for evidence tree
`629d1e5`; artifacts prove `2576/2576` on every hosted OS, Gitleaks 208/0,
CodeQL 52/0 with 0 exact-ref alerts, and all three reproducible version-0.1.211
unsigned packages. Earlier CI `33301715578` and CodeQL `33301715584` target
`c13acc5` and do not prove this row. By fault origin only AD Disconnect advances
from M to P; HC Disconnect remains M and CL Disconnect remains P. Other
Admission disconnect phases, disconnect-plus-cleanup faults, and native non-
cooperative teardown remain open. This is managed same-host/portable evidence,
not native API, physical two-Device, packaged accessibility, signed/notarized
package, or release proof. Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the Goal remain open.

### 5.31 2026-08-30 Host capture-start authenticated disconnect

For T03, T06, and T14, exact commit
`fe0be79e0accbbb0cd4eef27b62e12620a18eccf` adds authenticated
transport loss while capture Start still owns HC. Ready and bilateral verified
`FSM1` exist, but Admission is unpublished. Capture emits one pre-Admission
frame and observes that owner disposed exactly once; the boundary hook runs
before Start returns.

Real participant connection disposal triggers the host callback barrier without
changing Trust, fingerprint, or `mirror.view`. The old generation becomes non-
current and unreacquirable; Admission/send/render/input remain zero. Host Start
awaits its owned cleanup before returning; the test then joins disconnect and
session completion outside the hook and verifies both nodes drained. Capture and
input Emergency Stop locally.

The exact RED expected `authenticated_connection_stale` but observed
`emergency_stop_won_start_race`. Disconnect had made the authenticated
Connection non-current and invoked its retained live callback, yet the later
controller Start result was projected first. The one-line fix revalidates
host facts after Start returns and before that projection. GREEN reports causal
stale with no inner/fingerprint. The same-generation media-mutation row now also
expects causal stale rather than `session_not_idle` after `RequestControlStop`.

Focused Debug/Release pass `1/1`; fresh rows pass `10/10` per configuration;
tracer, Desktop, and solution pass `36/36`, `713/713`, and `2577/2577`; builds
have zero warnings/errors; format/diff checks pass; and self plus two independent
reviews report 0 P0/P1/P2 findings. Exact commands and limitations are in the
[host capture-start disconnect evidence](../evidence/2026-08-30-host-capture-start-authenticated-disconnect.md).

Final exact-SHA CI `33303210427` and CodeQL `33303210391` pass for evidence tree
`a0c9648`; artifacts prove `2577/2577` on every hosted OS, Gitleaks 208/0,
CodeQL 52/0 with 0 exact-ref alerts, and all three reproducible version-0.1.213
unsigned packages. Earlier CI `33302708813` and CodeQL `33302708801` target
`17a3401` and do not prove this row. By fault origin only HC Disconnect advances
from M to P; AD Disconnect and CL Disconnect remain P. Other HC disconnect
phases and disconnect-plus-cleanup faults remain open. This is managed same-host/
portable evidence, not native API, physical two-Device, packaged accessibility,
signed/notarized package, or release proof. Tasks 5, 5.5a, and 5.5, aggregate
H0/H1 acceptance, `CreateProduction()`, every native/physical/signing/
notarization/release gate, and the Goal remain open.

### 5.32 2026-08-30 Host capture-start authority revoke

For T05, T06, and T14, exact test-only commit
`62e9372aef378e8c085ccf79502104f63ae8aa76` applies real host Trust
revocation while capture Start still owns HC. Ready and bilateral `FSM1` exist,
capture's initial frame owner disposes exactly once, and Admission/send/render/
input remain zero.

Fingerprint-bound `UpdateCapabilitiesAsync(..., CapabilityGrant.None)` returns
`Applied` and reaches the host callback barrier. Trust identity and fingerprint
remain, while `mirror.view` and the Capability set are empty. The old generation
is non-current and unreacquirable. The existing `fe0be79` revalidation preserves
causal `authenticated_connection_stale` with no inner/fingerprint; capture/input
Emergency Stop and both nodes drain. No further production change is required.

Focused Debug/Release pass `1/1`; fresh rows pass `10/10` per configuration;
tracer, Desktop, and solution pass `37/37`, `714/714`, and `2578/2578`; builds
have zero warnings/errors; format/diff checks pass; and self plus independent
review report 0 P0/P1/P2 findings. Exact commands and limitations are in the
[host capture-start authority-revoke evidence](../evidence/2026-08-30-host-capture-start-authority-revoke.md).

Final cumulative evidence tree `c4c02a3` contains this row, pairing fix
`7239448`, and rows through 39. CI `33305006486` and CodeQL `33305006421`
succeed with `2580/2580` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and
three verified reproducible unsigned packages. By fault origin only HC Revoke
advances from M to P; HC/AD/CL Disconnect remain P.
Other HC revoke phases and revoke-plus-cleanup faults remain open. This is
managed same-host evidence, not native API, physical two-Device, packaged
accessibility, signed/notarized package, or release proof. Tasks 5, 5.5a, and
5.5, aggregate H0/H1 acceptance, every native/physical/signing/notarization/
release gate, and the Goal remain open. `CreateProduction()` remains
unavailable.

### 5.33 2026-08-30 Host capture-start caller cancellation

For T06 and T13, exact test-only commit
`0f26c26e93c0af6013372245ba448fd839037a1c` injects exact caller
cancellation while capture Start owns HC. A dedicated token belongs only to
Start; a separate 20-second harness token bounds network work and joins.

After Ready, bilateral `FSM1`, and exact first-frame disposal, the hook
synchronously cancels the caller. Admission/send/render/input remain zero.
Authenticated authority remains current, the same generation can be probed and
immediately released, and Trust/fingerprint/sole `mirror.view` are unchanged.

Host Start awaits owned ordinary Stop, fail-close, connection disposal, and
cleanup before rethrowing the exact caller-token `OperationCanceledException`.
The test then joins participant session completion and verifies both nodes
drained. No production change is required.

Focused Debug/Release pass `1/1`; fresh rows pass `10/10` per configuration;
tracer, Desktop, and solution pass `38/38`, `715/715`, and `2579/2579`; builds
have zero warnings/errors; format/diff checks pass; and self plus independent
review report 0 P0/P1/P2 findings. Exact commands and limitations are in the
[host capture-start caller-cancellation evidence](../evidence/2026-08-30-host-capture-start-caller-cancellation.md).

Final cumulative hosted evidence is the successful `c4c02a3` tree and
`33305006486`/`33305006421` runs described above; `9ca4b2c` precedes this row.
By fault origin only HC Cancel advances from M to P; CL Cancel remains P. Other
HC cancellation
phases, foreign/tokenless cancellation, and cancellation-plus-cleanup faults
remain open. This is managed same-host evidence, not native API, physical two-
Device, packaged accessibility, signed/notarized package, or release proof.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every
native/physical/signing/notarization/release gate, and the Goal remain open.

### 5.34 2026-08-30 Local pairing lifetime-cancellation precedence

For T13, CI `33304022418` at exact tree `9ca4b2c` failed only macOS pairing
runtime cancellation precedence: a cancellation-ignoring late Enable observed
disposed state before asynchronous linked-cancellation propagation became
observable at classification, and surfaced `ObjectDisposedException` instead of
`OperationCanceledException`. Ubuntu, Windows, and Secret Scan passed; packages
were skipped. CodeQL `33304022374` passed 52/0 independently.

Fix `72394484e9fd0fd556497641f1ac5d79afe80bce` checks runtime lifetime
cancellation before linked cancellation and disposed state. Pairing tests pass
`29/29` in Debug/Release; the exact row passes twenty fresh processes per
configuration; and the combined tree through `858acb2` passes `2580/2580` with
zero build warnings/errors. This changes no Remote Window matrix cell. Details
are in the
[pairing lifetime-cancellation evidence](../evidence/2026-08-30-local-pairing-lifetime-cancellation.md).

Final post-fix tree `c4c02a3` succeeds in CI `33305006486` and CodeQL
`33305006421` with the cumulative hosted results described above.

### 5.35 2026-08-30 Host capture-start rejection

For T06 and T14, exact test-only commit
`858acb2c28321ed8603646227d8834eef318405a` returns bounded capture
rejection while HC owns Start. Ready, bilateral `FSM1`, exact first-frame
disposal, current connection, and same-generation probe all exist, while Trust,
fingerprint, Mirror grant, and transport remain unchanged.

`capture_start_failed` escapes without inner/fingerprint; Admission/send/render/
input remain zero; ordinary capture/input Stop runs with exact Emergency Stop
counts zero; and both nodes drain. No production change is required.

Focused Debug/Release pass `1/1`; fresh rows pass `10/10` per configuration;
tracer, Desktop, and solution pass `39/39`, `716/716`, and `2580/2580`; builds
have zero warnings/errors; format/diff checks pass; and self plus independent
review report 0 P0/P1/P2 findings. Exact commands and limitations are in the
[host capture-start rejection evidence](../evidence/2026-08-30-host-capture-start-rejection.md).

Final cumulative tree `c4c02a3` contains `858acb2` and `7239448`; CI
`33305006486` and CodeQL `33305006421` succeed. By fault origin only HC Reject
advances from M to P; HC Reject, Cancel, Revoke, and Disconnect are now P.
Other rejection phases and rejection-plus-cleanup faults remain open. Tasks 5,
5.5a, and 5.5, aggregate H0/H1 acceptance, `CreateProduction()`, every native/
physical/signing/notarization/release gate, and the Goal remain open.

### 5.36 2026-08-30 Host initial Authorization authenticated disconnect

For T03, T06, and T14, exact test-only commit
`077c996e82dd4077d24a58957c37b86383479f6e` injects an independent
authenticated transport loss while H0 still owns initial host facts. Real
protocol-1.7 loopback reaches a deterministic barrier after
`TrustMirrorAuthorizationSource` has acquired a fingerprint-bound Authorization
reservation but before its wrapper returns that owner to the coordinator.

At the frozen boundary, the Connection Preparation registration and live
revocation callback are current. The Permission reservation exists, while H1
Protection, Emergency Stop, route, Prepare, capture, Admission, media
attachment/send, render, and input authority remain unopened. Participant
Connection disposal reaches
the real host callback: Connection and its Preparation registration become
non-current and the old generation cannot be reacquired. The still-wrapper-owned
Authorization registration, Trust identity, fingerprint, and sole
`mirror.view` grant remain current, proving transport loss rather than Trust or
Capability revocation.

Releasing the barrier performs the normal sole ownership handoff. The
coordinator receives and disposes the Authorization registration, observes
Connection stale, and exposes bounded `authenticated_connection_stale` without
inner, fingerprint, or dependency data. No route was selected, so fail-close is
zero and Connection disposal is exactly once. No route or session authority
opens, and both nodes' complete owner graphs drain while the exact source lease
and unchanged Trust grant remain current. The wrapper preserves pre-handoff
ownership by disposing any acquired registration on an exceptional exit; only
normal return transfers it to the coordinator.

Focused Debug/Release pass `1/1`; fresh rows pass `10/10` per configuration;
tracer, Desktop, and solution pass `40/40`, `717/717`, and `2581/2581`; builds
have zero warnings/errors, and format/diff checks pass. Exact commands and
limitations are in the
[host initial Authorization authenticated-disconnect evidence](../evidence/2026-08-30-host-initial-authorization-disconnect.md).

Exact-SHA CI `33305848081` and CodeQL `33305848085` succeeded. Downloaded
artifacts prove `2581/2581` with every non-success counter zero on each hosted
OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref open alerts, and all three
reproducible unsigned packages verified. By fault origin only H0
Disconnect advances from M to P; H1 Disconnect remains M and CL Disconnect
remains P. Other pre-route disconnect phases and disconnect-plus-cleanup faults
remain open. The local tracer is same-host managed macOS evidence and the hosted
matrix remains managed/contract evidence; neither is native API, physical
two-Device, packaged accessibility, signed/notarized package, or release proof.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance,
`CreateProduction()`, every native/physical/signing/notarization/release gate,
and the Goal remain open.

### 5.37 2026-08-30 Host route authenticated disconnect

For T03, T06, T07, and T14, exact commit
`d5931817d95b592bfa4e22eb8da304a18c86e2ca` injects independent
authenticated transport loss after a real H1 responder-route side effect but
before any protocol Prepare call. Real authenticated protocol-1.7 loopback has
one host route, current exact Connection Preparation, reserved Protection, and
current Emergency Stop readiness when the hook runs.

Participant Connection disposal reaches a barrier only after the production
host revocation callback returns. Connection and its Preparation registration
are non-current and unreacquirable, while Trust identity, fingerprint, and sole
`mirror.view` remain unchanged. Host Start returns only bounded
`authenticated_connection_stale`; the Prepare method and wire admission remain
zero. No attachment, capture, Admission, media send, render, or input authority
opens. The owned route is fail-closed once, Connection is disposed once, and the
two-node owner graph drains.

The exact RED expected `PrepareCount` 0 but observed 1. The first minimal
post-route fact read made that row GREEN, but strict counter-review found it did
not preserve every cancellation, terminal, deadline, Protection/Emergency, and
failure-priority case. The final gate checks caller cancellation, the recorded
terminal cause, and deadline; revalidates current host facts and fresh exact-
source `Safe` Protection; then repeats cancellation, terminal, and deadline.
Non-fatal concurrent validation failure yields the recorded terminal reason,
while `OutOfMemoryException` remains the exact primary entering the existing
outer cleanup/aggregation path.

Focused H1 Debug/Release pass `1/1`; the H1 row plus coordinator class pass
`116/116`; tracer, Desktop, and solution pass `41/41`, `718/718`, and
`2582/2582`; builds have zero warnings/errors, and format/diff checks pass.
Exact commands and limitations are in the
[host route authenticated-disconnect evidence](../evidence/2026-08-30-host-route-authenticated-disconnect.md).

Exact CI `33306962398` failed only macOS Transport
`ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler(Omit)`: macOS
passed `2581/2582` overall and Desktop `718/718`, while Ubuntu and Windows each
passed `2582/2582` and Secret Scan passed 208/0. CodeQL `33306962391` succeeded
52/0. Packages were skipped. Test-only `c98a570` changes only the theory's
handshake/failure/outer bounds from 300 ms/2 s/3 s to 2 s/4 s/6 s; all security
assertions remain. Focused Debug/Release pass `3/3`, ten fresh Release processes
pass `30/30`, Transport passes `755/755`, and strict review reports APPROVE.
Exact-SHA CI `33307322868` and CodeQL `33307322870` then succeeded: downloaded
artifacts prove `2582/2582` with every non-success counter zero on each hosted
OS, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref open alerts, and all three
reproducible unsigned packages verified.

By fault origin only H1 Disconnect advances from M to P; H0 and CL Disconnect
remain P. Other H1 route/send phases and disconnect-plus-cleanup faults remain
open. The local tracer is same-host managed macOS evidence and the hosted matrix
remains managed/contract evidence; neither is native API, physical two-Device,
packaged accessibility, signed/notarized package, or release proof.
Tasks 5, 5.5a, and 5.5, aggregate H0/H1 acceptance, every native/physical/
signing/notarization/release gate, and the Goal remain open. `CreateProduction()`
remains unavailable.

### 5.38 2026-08-30 First bounded cleanup-confirmation vertical

For T06, T13, and T14, implementation commit
`685225ed92b76ee2e6f4800b9c97f8baf2af378d` adds the first narrow
production-composed cleanup-confirmation watchdog. A managed two-node session
reaches active final Admission over real authenticated protocol 1.7 and
bilateral verified `FSM1`, sends and renders an encrypted frame, and then loses
the participant authenticated-control connection independently. The production
revocation callback closes admission and synchronously creates the generation's
single cleanup timer before returning. The coordinator removes the generation
from active authority, retains it as retiring, and starts the one real cleanup
task.

The tracer blocks the host Connection's owned `DisposeAsync` before it forwards
to the real managed connection. This proves a genuinely unsettled owner rather
than a delayed test assertion. The injected `TimeProvider` advances first to
one tick before the ten-second deadline: cleanup remains retiring, the stable
terminal failure is still absent, and a replacement Start remains pending with
zero route, Prepare, Admission, capture, renderer, media, input, permission,
Protection, or Emergency Stop authority. At exact equality, the watchdog wins,
publishes the stable `host_cleanup_timeout` failure, and releases the shared
bounded confirmation. The waiting replacement is rejected with
`host_cleanup_unconfirmed`; its newly supplied resources are disposed without
opening authority.

Releasing the blocked Connection later lets the original real cleanup continue
to completion. Both managed node graphs, routes, media budget, renderer,
capture/input, protection, registrations, control generation, and connections
drain; the retiring reference clears and the sole timer is released. Neither
late success nor a second replacement Start clears the monotonic latch. The
second Start is rejected before authority, and coordinator disposal exposes the
same timeout failure instance.

Local macOS Debug and Release verification passes the production-composed
tracer `42/42`, Desktop `720/720`, and complete solution `2584/2584`. Both
warning-as-error solution builds report zero warnings and zero errors. Format,
explicit composition validation, the deterministic simulator, and the direct
and transitive dependency-vulnerability audit pass. Exact implementation CI
`33311180093` and CodeQL `33311180128` succeed. Downloaded artifacts prove
`2584/2584` with every non-success counter zero on each hosted OS, Gitleaks
208/0, CodeQL 52/0 with zero exact-ref open alerts, and all three reproducible
unsigned packages verified.

By fault origin this advances only CL Timeout from M to P. It
does not cover a non-cooperative synchronous Emergency Stop prefix before the
watchdog is armed, explicit Stop- or Dispose-first termination, timer setup or
release failure, cleanup winning the equality race, late cleanup fault or OOM,
pre-generation cleanup, or the complete cross-boundary reject/throw/cancel/
timeout/revoke/disconnect/cleanup-fault matrix. The evidence is same-host
managed loopback, not native API, physical Windows/macOS/Linux, packaged
accessibility, signing, notarization, or release acceptance. Tasks 5, 5.5a, and
5.5, aggregate acceptance, every later release gate, and the long-term Goal
remain open. `CreateProduction()` must continue to report Remote Window
unavailable.

### 5.39 2026-08-30 External Dispose-first bounded cleanup confirmation

For T06, T13, and T14, exact implementation
`ea984fb01cad46ab128c6d294835df59327aa8ac` extends ADR 0028 to one explicit
external Dispose-first order. The coordinator begins with a stable active
generation and an uncontended lifecycle gate. The first external Dispose sets
the disposed gate, then its worker closes admission and publishes
`active -> retiring`, one real cleanup task, one confirmation operation, and the
single watchdog before invoking any potentially blocking controller or owner
boundary.

The deterministic fixture blocks the original host Connection disposal. A later
cross-thread authenticated-disconnect callback enters the existing capture and
input Emergency Stop prefix, closes the control generation, and attaches cleanup
exactly once to the already-published operation. A post-claim frame is disposed
without a second media send. Snapshot, media authority, Permission observation,
Protection, Emergency Stop registration, fail-close, controller Stop, and
Connection-disposal assertions prove that no replacement owner graph is
created.

Start after explicit Dispose returns `ObjectDisposedException` before route,
Prepare, Admission, capture, input, or authority counts can advance. At T-1, the
public Dispose callers, real cleanup, retiring generation, and sole timer remain
pending. Exact equality publishes one stable `host_cleanup_timeout`. Concurrent,
later, and post-drain external Dispose calls share the same public Task and
exception instance. Releasing the blocked Connection clears retiring ownership
and the timer, while the completed public outcome and disposed Start gate remain
immutable.

Local Debug and Release verification passes the focused row `1/1`, twenty fresh
focused processes `20/20`, coordinator `117/117`, Desktop `721/721`, and full
solution `2585/2585`; warning-as-error builds and all supporting quality gates
pass. Exact-SHA CI `33314229467` and CodeQL `33314229459` succeed. Downloaded
artifacts prove `2585/2585` with every non-success counter zero on all three
hosted OSes, Gitleaks 208/0, CodeQL 52/0 with zero exact-ref open alerts, and
three reproducible version-`0.1.222` unsigned packages. Exact commands, jobs,
artifacts, digests, and limits are in the
[Dispose-first evidence](../evidence/2026-08-30-dispose-first-bounded-cleanup.md).

This closes only Task 5.5a.3a. It adds direct evidence within the already-
Partial CL Timeout cell, but adds no 43rd production-composed tracer and changes
no matrix status. Stop-first, lifecycle-gate contention, cleanup-winner/equality
races, timer faults, late cleanup fault/OOM, pre-generation cleanup, and every
other owner remain open. This is managed contract evidence, not native API,
physical two-Device, packaged accessibility, signing, notarization, or release
proof. Tasks 5, 5.5a.3, 5.5a, and 5.5, every native/physical/release gate, and
the Goal remain open. `CreateProduction()` remains unavailable.

## 6. Security state machine rules

- `Discovered` is never equivalent to `Paired`.
- `Paired` is never equivalent to capability-authorized.
- Capabilities are evaluated for every new operation and driver lease. A revoked
  Swap may only finish through Exact Recorded Decision Convergence bound to the
  durable Operation/correlation/peer record; this is not authority for new work.
- Trust revocation removes authorization and active-session eligibility before
  waiting for shutdown; every affected registered session receives a stop
  request before the revocation call returns, with failures surfaced.
- Host Remote Window Preparation authorization binds the authenticated
  connection's exact peer fingerprint and all role-required Mirror Capabilities
  to one revocable reservation. Any Applied Trust mutation makes that
  reservation terminal even when the resulting grant is value-equal.
- Host Remote Window Preparation permission binds one exact prompt-free owner,
  revision, capture/input fact set, and frozen role. A later accepted permission
  revision makes that reservation terminal; regrant cannot revive it.
- Host Remote Window Preparation Connection authority binds one exact
  authenticated generation and its exact media session in a composite
  reservation. Route selection and Prepare send admission must carry that exact
  owner; generation or media mutation makes it terminal, while the overlapping
  exact-once live callback owns post-promotion shutdown.
- Host Remote Window Preparation Protection authority binds the full accepted
  observation and freshness interval to one exact source registration. The same
  owner progresses through temporary, formal-pre-start, and live capture-start
  admission; live mutation closes new frame/input use admission before queued
  notification, and a Safe result cannot reopen it until exact older uses drain.
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
