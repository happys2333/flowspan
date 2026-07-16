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
| T04 | Replay/duplicate command or mismatched receipt | Session epoch, message/operation IDs, one atomic pending correlation reservation across Activity message types, bounded TTL, request/payload/descriptor digests, durable idempotency journal, and payload-free receipts/results bound to authenticated participants, purpose, Operation, Activity, and descriptor | property/fault tests, digest tamper, cross-type correlation collision, unsolicited/wrong-correlation/wrong-Activity result tests |
| T05 | Capability escalation | Deny by default; typed independent capabilities with documented peer-relative direction; read immutable trust projections; apply the complete edited grant only through the coordinator; explicit all-of/any-of session admission checked before connect and again after authentication; exact operation direction checked immediately before metadata/payload disclosure or use; revoke/downgrade persists first and drains or rejects every active handler whose requirement is no longer satisfied | permission matrix, any-of admission/final-alternative removal, immutable-snapshot, conditional-mutation, stop-failure, connection-registration race, and outbound/inbound Activity authorization and inventory-revocation tests |
| T06 | Remote input after authority loss | Monotonic driver lease epochs, short expiry, local enforcement, emergency-stop epoch bump | lease model/property tests |
| T07 | Sensitive content capture | Continuous protection-state probe, fail closed on unknown/stale, visible pause/blank state | platform contract + native manual tests |
| T08 | Malformed/oversized or cross-protocol input | classify only bounded `FSP1`/hello and `FSH1`/hello initial frames; transfer the pre-read frame to exactly one decoder; enforce frame, depth, field, count, decompression, timeout, and allocation limits; decode protocol-1.2 Finished and protocol-1.3 `FSR1` KeyUpdate only after AEAD authentication and require their canonical bounded shapes | hostile selector/codec corpus, Finished and KeyUpdate wrong-kind/flag/length/trailing/tamper rejection, and protocol/state property tests |
| T09 | Descriptor opens dangerous target | schema validation, URL scheme allowlist, safe filename handling, no source paths, confirmation where needed | adapter security tests |
| T10 | Secret leakage in logs or history UI | structured allowlist logging and recovery projection, redaction before sinks, no payloads/raw input/private keys/request digests/exception text, bounded target-local display | redaction canary and recovery-snapshot projection tests |
| T11 | Journal tampering/rollback | restricted storage, authenticated records or database integrity, monotonic revisions, recovery conflict state, fail-closed startup presentation without blocking unrelated Activity work | persistence tamper and Desktop startup-fault tests |
| T12 | Compromised dependency/update | lock files, dependency review, hashes/signatures, SBOM, signed artifacts and update metadata | CI supply-chain checks |
| T13 | Resource exhaustion | per-peer rate, concurrency and size limits; at most 128 total inbound connection slots, with independent bounded pairing and authenticated-session capacity; pairing messages limited to 4096 bytes and the whole pairing ceremony limited to two minutes by default/ten minutes maximum; the process-scoped in-memory operation journal binds at most 4,096 IDs and rejects an unknown ID before handler work when full while preserving known retries; Replace inventory limited to one strictly ordered page of 64 snapshots; bounded initial-family selection and handshake timeouts, queues, and DNS-SD record/instance/address caches; serialized reconnect loop with bounded backoff and coalesced network-change events; cancellation and backpressure | selector/pairing/session/inventory/journal capacity and deadline tests, hostile input, load/fault/DNS-batch, timeout, and reconnect-churn tests |
| T14 | Invisible monitoring | foreground sharing indicator on every participant, no unattended defaults, audit receipt, immediate local stop | UI accessibility/e2e tests |
| T15 | Downgrade to unsafe fallback | authenticated feature negotiation; signed highest-common-version offers; require encrypted Finished for mutually supported protocol 1.2+ and bounded live rekey for 1.3+; retain 1.2 as a reconnect-at-bound compatibility path and 1.0/1.1 as named legacy modes; explicit capability and confirmation | altered-version signatures, 1.2 Finished sequence proof, 1.3 threshold/crossed-request rekey tests, compatibility presentation, and downgrade tests |
| T16 | Future relay reads content | application-layer E2E encryption independent of byte-forwarding relay | relay-as-attacker integration test |
| T17 | Hidden or premature platform/network privilege | request no capture/input/network privilege at launch; require feature-scoped rationale, exposed-data disclosure, prompt expectation, revocation path, and affirmative acknowledgement before the privileged boundary; cancel remains side-effect free | platform-guide, command-gating, no-start review/cancel, disable-reset, and Headless keyboard tests plus native grant/deny/revoke evidence |
| T18 | Destructive Replace without recoverable target state, or forged/stale target/undo metadata | purpose-scoped authenticated target inventory; distinct `activity.replace` capability and destructive message; exact target ID/revision/digest revalidation; Adapter capture plus application verification and target-owned store before resume; payload-free capsule reference; bounded expiry; exact-current replacement check; idempotent consume; bounded unresolved-first recovery snapshot; exact terminal-history reduction for the supported semantic tracer; global pending/`Recovering` fail-closed gate; private target-local endpoint plus service-level live eligibility preflight; destructive desktop activation blocked until target snapshot, confirmation, recovery, and undo are visible | inventory filtering/bounds/revocation, capture/store/revision/digest negatives, terminal-graph conflict/orphan/pending tests, recovery projection/truncation/startup-fault tests, direct-call preflight, retry/expiry/consume/stale reason tests, strict codec tamper tests, authenticated result binding, acknowledgement-loss and encrypted-loopback tests |

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
| T05 | A control channel requires local `activity.offer` or `activity.receive`; old all-of profiles remain strict, removing one any-of alternative keeps the session, and removing the final alternative drains it. Complementary one-way grant directions work under the deterministic smaller-Device-ID connector election. An idle authenticated channel is never called sharing. | Physical active-session shutdown across sleep/wake and credential-store failure. |
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
