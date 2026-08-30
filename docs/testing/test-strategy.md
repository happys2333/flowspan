# Flowspan Test Strategy

## 1. Evidence model

No single test layer proves Flowspan. Release evidence records the command,
commit, OS/architecture, runner or hardware, result, and artifact link. Results
are classified as:

- **Local**: executed on the current developer machine;
- **CI**: executed on a named hosted runner image;
- **Real machine**: executed with the relevant native permissions and hardware;
- **Simulated/contract**: proves portable logic or adapter conformance, not a
  platform API itself.

A result in one class never silently substitutes for another.

## 2. Mandatory layers

| Layer | Purpose | Required examples |
| --- | --- | --- |
| Formatting/build | deterministic, warning-free source | `dotnet format --verify-no-changes`, Release build |
| Unit | value objects and local transitions | descriptor validation, capability sets, leases |
| Model/property | broad transition sequences and invariants | operation idempotency, swap atomicity, lease epochs |
| Protocol contract | stable codec/version behavior | golden fixtures, unknown/required fields, limits |
| Integration | multiple real components through in-memory ports | two-node handoff/move/swap/mirror |
| Fault injection | safety at every I/O boundary | drop, duplicate, reorder, delay, disconnect, journal failure |
| Security negative | reject hostile or unauthorized behavior | key change, replay, bad grant, malformed frames, redaction |
| Platform contract | every native adapter follows common semantics | denied permission, protected surface, emergency stop |
| Native smoke/e2e | actual APIs and packaging | matching Windows/macOS/Linux runners and machines |
| UI/accessibility | user-visible state and controls | keyboard flows, accessible labels, no color-only state |

## 3. Deterministic simulator

The simulator owns a virtual monotonic clock and seeded schedule. Network events
are explicit (`deliver`, `drop`, `duplicate`, `disconnect`) and every node owns
an independent journal. Failing tests print the seed and minimized event trace.
No property test depends on wall-clock sleeps.

DNS-SD tests keep the third-party packet stack behind an injected record
boundary. Core tests cover canonical TXT limits, hostile randomized payloads,
current-trust binding, dual-stack address selection, expiry, and removal.
Adapter tests cover split SRV/TXT/A/AAAA arrival, batch/cache bounds, package
record translation, full-stack restart on network change, and injected
bind/factory/diagnostic failures. Publisher tests cover canonical minimized
profiles, immediate publication, fresh-nonce refresh, cancellation withdrawal,
failure preservation (including simultaneous startup/cleanup failure),
network-stack replay, old-stack cleanup recovery, and failed-publish rollback.
These are contract tests; only the manual two-device matrix may be labelled
physical multicast evidence.

Inbound-listener integration tests open one real loopback TCP listener and
authenticate two different current trust records through the same port. Negative
and fault tests reject an unknown peer without poisoning the next accept, prove
that a claimed Device ID cannot substitute another key, enforce the default and
hard concurrency limits with slot backpressure, isolate handler failure, reject
missing capabilities, and drain the correct sessions on peer revocation, caller
cancellation, or fatal accept failure. Fake accept/session ports make failure
ordering deterministic. These tests prove the bounded listener contract only;
they do not prove physical two-device networking, firewall behavior, or remote
interface reachability.

Pairing tests freeze a canonical `FSP1` hello fixture and run a seeded hostile
corpus through every bounded decoder. Deterministic in-memory ceremonies cover
highest-common-version selection, matching SAS requests, directional local
grants, peer rejection canceling a pending prompt, network and prompt deadlines,
transcript-signature/confirmation/completion tamper, verified identity conflict,
same-key re-pairing, trust-save failure, caller cancellation, and simultaneous
protocol/cleanup failure. A real loopback TCP test carries the complete ceremony
through two independent trust stores. The decision ports simulate user choices;
these tests do not prove that two people compared SAS values or that a desktop UI
is safe and accessible.

Unified-listener tests classify only `FSP1`/hello and `FSH1`/hello as valid first
frames, reject truncated, wrong-kind, unknown, and hostile selectors, and prove
exclusive ownership of the pre-read frame. Real loopback coverage pairs and then
reconnects through the authenticated branch on the same published port. A
blocked pairing decision must not block an already trusted peer; pairing/session
overload is isolated, profiles enforce the hard total bound, and cancellation
must drain both branches. These remain same-host transport and decision-double
tests.

The task 4.3a secure-session slice adds protocol 1.2 above the existing 1.0/1.1
compatibility path. A frozen 62-byte `FSH1` Finished plaintext and SHA-256 hash
bind role, authenticated transcript, and 16-byte session identifier. Each peer
then protects that plaintext as its epoch-1 `FSE1` sequence-zero frame before
control upgrade. Security tests cover exact encoding, wrong role/transcript/
session, malformed lengths, trailing data, AEAD tamper without counter advance,
and the resulting sequence-one control boundary. A narrow Finished-transaction
transport seam injects initiator and responder send failure, missing receive,
AEAD tamper, and valid-ciphertext binding mismatch; every failure must destroy
the authenticated frame session and close the transport. If transport cleanup
also fails, the result must preserve both the primary and cleanup causes. Real
TCP tests cover both Finished directions, omission under the whole-handshake
deadline, tampered
or wrongly bound peer Finished as a structured authentication failure, inbound
socket close, and the rule that failed responder authentication never reaches
Trust registration or the session handler. Legacy 1.1 stays at sequence zero
and the desktop snapshot explicitly names its degraded legacy-compatibility
mode. A production desktop reconnect loop negotiates 1.2 on loopback; these
results prove same-process key confirmation, not independent cryptographic
review, physical interception resistance, or live rekey.

The task 4.3b slice introduces protocol 1.3 without changing the frozen 1.2
Finished transaction. Security tests freeze the 10-byte `FSR1` update and
direction/session/next-epoch HKDF vectors, then prove monotonic epoch reset,
retired-key rejection, replay/gap rejection, and old-key erasure in the state
owner. Transport tests inject update write, flush, decode, authentication,
disconnect, cancellation, timeout, and cleanup failures. Deterministic duplex
tests cover unilateral request/response, crossed-request suppression, repeated
updates, and automatic 2^20-frame/1-GiB thresholds through reduced injected
limits. A real authenticated protocol-1.3 loopback repeatedly rekeys both
directions, includes crossed requests, and continues carrying identity/version-
bound control messages. The production profile prefers 1.3; 1.2 remains
interoperable, is presented separately from 1.0/1.1 legacy mode, and reconnects
rather than exceeding the bound. Hosted results remain contract evidence, not
independent cryptographic review or physical hostile-LAN evidence.

Desktop-shell tests keep the production XAML and view model behind Avalonia's
headless platform while retaining the repository's xUnit v2 runner. They cover
protected/degraded identity presentation, redacted startup failure, explicit
sharing and stop availability, declared automation names, keyboard activation,
and close-during-startup cancellation. Every CI OS also runs the executable's
`--validate-composition` mode with an explicitly degraded in-memory identity;
the output must name TEST MODE. That command proves composition and process exit,
not a native window, platform credential store, screen reader, high-contrast
theme, or real permission behavior.

Avalonia 12.1.0's `HeadlessUnitTestSession.StartNew` can publish a session
before assigning the dispatcher task stored by that session. Per-test session
disposal can therefore throw a `NullReferenceException` even after the test has
observed the production window's `Closed` event. Flowspan uses Avalonia's
assembly-cached headless session and explicitly declares `PerTest` application
and Dispatcher isolation: only the session dispatcher is process-scoped, while
each `Dispatch` creates and disposes a fresh application and Dispatcher scope.
Window tests still wait for the real production `Closed` event. Repeated local
runs and the hosted OS matrix are required because a single passing process
cannot disprove the upstream construction race.

The task 7.2a desktop pairing tests call the same `IPairingDecisionSource` port
as the security ceremony. Unit tests cover one-visible-prompt enforcement,
explicit zero-capability defaults, code-comparison gating, cancellation,
disposal, stale commands, and deliberately reordered UI callbacks. Integration
tests run two complete ceremonies over a real loopback TCP connection: Trust is
absent before both decisions, each side persists only its local selected grant,
and one rejection leaves both stores empty. These tests do not make the
production desktop start its listener or prove a human compared two devices.

The task 7.2d trusted-reconnect tests keep the user-triggered network lifetime
outside the transport primitives. Deterministic coordinator doubles cover
Device-ID connector election, current Trust ordering, waiting/authenticating/
authenticated-idle/retry/permanent states, capability upgrade and downgrade,
revoke, cancellation/drain, conflicting-fingerprint latching, sanitized worker
failure, and the rule that discovery refresh cannot interrupt an active
authenticated channel. Candidate-source tests reconstruct the public key only
from current Trust and require the signed offer to verify before returning an
endpoint. A same-process loopback theory composes the production reconnect
supervisor, authenticated connector, listener, both Trust coordinators, and both
handlers with complementary one-way Capability directions under the deterministic
smaller-Device-ID connector election; both
peers must say `AUTHENTICATED — IDLE / NOT SHARING`. Security and transport tests
separately prove explicit all-of versus any-of admission and that an any-of
session drains only after its final alternative is removed. Headless tests verify
the per-peer and warning text/automation surface. These results do not prove
physical DNS-SD, firewall behavior, sleep/wake, interface churn, native
notifications, or two-machine identity replacement.

The task 7.2e permission-preflight tests treat the local-network runtime factory
as the side-effect boundary. Windows, macOS, and Linux guide cases must name the
exact minimized discovery exposure, a platform-specific prompt/firewall
expectation, and a revocation route. View-model tests prove that direct enable,
review, and cancel cannot cross the boundary before acknowledgement; Disable
clears the acknowledgement; failure reopens the reviewed recovery surface; and
dispose still cancels an admitted enable. A Headless keyboard test traverses
review, acknowledgement, and enable while checking automation names and the
persistent `NOT SHARING` state. These are selection, state, and UI contracts,
not evidence that a native prompt appeared, permission changed, a firewall rule
worked, or settings revocation succeeded.

The task 7.3a Activity tests use one `workspace.note/v1` tracer bullet. Codec
tests round-trip the bounded transfer and payload-free receipt, reject wrong
targets, tampered descriptor digests, overlong envelope deadlines, wrong
recipients, and correlation mismatch. Session tests cover authenticated sender
binding, target resume, exact pending-receipt binding (including wrong Activity),
unsolicited receipt, acknowledgement loss, and a real encrypted loopback
Handoff that preserves the source. Application and desktop-runtime tests enforce
source-side `activity.receive`, target-side `activity.offer`, target liveness,
idempotency, target rejection of empty or malformed-shape portable notes, and no
outbound payload before authorization. View-model and Avalonia Headless tests
prove the explicit source-preserving preview, named
Remote Window limitation, keyboard flow, payload-free receipt projection, and
unchanged `NOT SHARING` band. Workspace tests prove identity -> Trust -> Activity
initialization, Activity-only retry, and network -> Activity -> Trust -> identity
disposal. These are deterministic and same-host results, not physical two-device
or arbitrary-application migration evidence.

The task 7.3b tests reuse that bounded descriptor and control channel for Move.
Application fault injection proves target resume precedes source close, target
rejection and delivery/acknowledgement loss preserve the source, duplicate
delivery is idempotent, and source-close failure becomes a committed duplicate
warning. Desktop-runtime tests cover missing source-side `activity.receive`, no
live authenticated channel, encrypted loopback success, and a live authenticated
target-side `activity.offer` rejection. View-model tests distinguish committed,
committed-with-warning, rejected, unavailable, and uncertain outcomes while
keeping receipts payload-free; Avalonia Headless drives the separate disabled/
enabled Move control by keyboard and verifies operation-neutral automation names.
These layers do not replace physical two-device interruption, packaged native
accessibility, or arbitrary application Adapter evidence.

The task 3.3a Swap core writes a payload-free coordinator intent before either
Prepare and writes a participant-bound Commit/Abort before decision delivery.
Generated two-participant cases cover prepare/decision drop, acknowledgement
loss, duplication, delayed expiry, reordered Abort, overlapping reservation,
Operation reuse, and recovery without a mixed terminal state. Persistence tests
reconstruct undecided and committed journals, inject intent/decision save
failure both before and after an atomic write, require reopen after every
ambiguous save, reject digest/shape/bounds tamper, and pin cross-platform request
and decision digests. The production payload contract uses a Swap-specific
AES-256-GCM atomic file and independent DPAPI, Keychain, or Secret Service key;
native DPAPI/Keychain tests run only on matching hosts and Linux Secret Service
remains a controlled invocation contract. Endpoint persistence is covered by
task 3.3b and authenticated wire/capability composition by task 3.3c below;
Desktop recovery, physical LAN loss, and abrupt power failure remain later
evidence.

The task 3.3b endpoint slice writes one Device-bound reservation with complete
original and incoming Activity snapshots before Prepared can be acknowledged,
then writes the participant-bound Commit or Abort before catalog mutation or
acknowledgement. Restart tests reconstruct Prepared, Commit, Abort, and
Abort-before-Prepare records; prove exact-original reduction, exact
already-applied replay, conflict recovery, unresolved overlap exclusion, and
coordinator-plus-two-endpoint convergence after dropped delivery. Fault tests
inject saves before and after publication and require reopen after every
ambiguous outcome. Hostile payload cases cover unknown fields, Device mismatch,
duplicate and noncanonical record order, record/byte bounds, enum and UTC
violations, request/descriptor/decision digest tamper, and participant-token
mismatch. The protected `FSEF` file and independent Windows/macOS/Linux key
purposes have shared and platform contract tests; only the matching host may
claim its native credential API. The Activity catalog remains an external
Adapter boundary. Task 3.3c below adds authenticated Swap transport; Desktop
recovery, physical LAN interruption, abrupt process termination, and power loss
remain later evidence.

The task 3.3c transport slice requires negotiated protocol 1.1 for all six Swap
messages while preserving non-Swap protocol 1.0 fallback. A committed fixture
freezes fixed message/correlation IDs, complete canonical JSON frames, body
digests, and frame SHA-256 hashes for snapshot/result, Prepare/result, and
decision/result. Every schema has unknown-field or hostile binding/digest tests;
protocol tests reject downgraded 1.0 Swap frames.

Session tests share the correlation registry with Handoff, Move, Replace, and
inventory; reject unsolicited and cross-operation results; and use a manual
`TimeProvider` to prove blocked send and silent-response expiry at the
snapshot/Prepare deadline or 30-second decision-acknowledgement window without
wall-clock sleeps. Timeout releases pending state, returns acknowledgement loss,
and closes the session; a receive-point clock check rejects a response arriving
at the deadline even when the timer callback is deliberately delayed.
Inbound-envelope tests prove an expired unknown Abort cannot create a durable
tombstone despite current authority.
Send tests include a connection that ignores cancellation, and an early-response
race proves a failed old send cannot release a newer cross-operation correlation
owner. Concurrent Cancel/Dispose is required to remain idempotent.
Authorization tests keep
`activity.swap` independent, block sensitive or forged direct Prepare, deny
unknown decisions, and allow post-revocation convergence only for an exact
durable Operation/correlation/peer binding. Journal format-v2 restart tests reject
v1 records without this evidence plus missing, null, wrong-type, duplicate, and
non-canonical binding fields. A real encrypted TCP loopback drives one local
direct and one remote durable endpoint from exact snapshot through intent,
Prepare, Commit, and catalog convergence. These are same-host contracts; Desktop
selection/recovery, physical devices, process kill, power loss, and native
application Adapters remain open.

Endpoint capacity tests fill the journal with near-maximum Activity descriptors,
then persist a terminal decision for every admitted Prepared record. Prepare
also rejects `long.MaxValue` incoming revisions before any write, and restart
remains openable. Missing numeric enum fields and non-canonical timestamp aliases
are hostile payloads rather than default values. Protected-file tests require a
pre-cancelled missing-file load to throw before key or filesystem access.

The task 3.4 deterministic model/fault slice adds continuous generated
transitions around the earlier scenario tests. Thirty-two fixed seeds execute
128 Operation-journal events each and assert digest one-to-one identity,
terminal receipt immutability, handler non-reexecution, and retryable
Failed/Recovering outcomes after every event. Move enumerates every three-event
combination of normal, drop-before-delivery, acknowledgement-loss, and duplicate
delivery, appends a normal retry, and asserts source/target safety after each
attempt. The existing Swap matrix enumerates every two-participant
Prepare/Decision fault combination. Mirror uses another 32-by-128 seeded model
covering role change, removal, transfer, expiry, emergency stop, and resume while
proving no retired lease epoch revives. Failures print a replayable seed/event or
complete fault trace; no property depends on sleeps or an unseeded random source.
Handoff now injects operation-journal failure before Adapter/catalog mutation;
Handoff and Move also wrap a real in-memory journal with a write-after-result
failure and prove that retry replays without duplicate Adapter/catalog work.
The journal reference model permanently binds the first request digest even
when Failed, Recovering, or a handler exception remains retryable. A capacity
test fills the process-scoped journal, proves an unknown Operation fails before
handler work, and proves a known same-digest retry remains eligible. Existing
Replace and Swap persistence tests cover pre-write and ambiguous post-write
failure. Control-session tests cover disconnect and bounded delay.
This closes the implemented-core task, not the release-wide criterion for
Scene apply, Remote Window, native Adapters, or physical fault evidence.

The task 7.3c tracer keeps Replace separate from Activity transfer. Application
tests require an exact target ID/revision/digest and prove capture or store
failure blocks before incoming resume, successful Replace stores a 15-minute
target-owned capsule, retries do not repeat capture/resume, and undo is
expiry-aware, exact-current, idempotent, and single-consume. The query-only
target-inventory slice filters sensitive, restricted, inactive, non-local,
different-kind, and unsupported Activities; returns only strictly ordered
payload-free snapshots; bounds a truncated page to 64; and rechecks source
`activity.receive` plus target
`activity.replace`, including same-session revocation. Strict query/result codec
tests cover purpose/participant/deadline/capture binding, unknown fields,
malformed digests, oversize arrays, and rejected-result non-disclosure. Session
tests make Transfer, inventory, and Replace correlation IDs globally exclusive,
fault closed on unsolicited results, classify lost acknowledgement as
uncertain, and exercise a real encrypted loopback inventory. Destructive
protocol tests retain target-snapshot/capsule tamper and encrypted-loopback
coverage. The durable state tests reconstruct the store and catalog to prove
exact Replace/undo replay, persisted-pending recovery without duplicate Adapter
calls, cleanup/store/digest/tag fault behavior, and concurrent retry. Platform
contracts keep a random key in DPAPI, Keychain, or Secret Service and put
descriptors only in an AES-256-GCM atomic file; local macOS also exercises a
disposable real Keychain item. After preview, recovery, and visible local undo
became available, Desktop composes the protected target peer and source command.
Runtime tests re-query and match the exact target at send time, recheck both
directional Trust grants, commit over a real encrypted loopback, project the
authenticated receipt/capsule, reject an unresolved target without a new
journal entry, and preserve the source. An endpoint concurrency test proves a
second distinct Replace cannot enter the journal while another Replace or local
undo owns the shared destructive boundary. View-model and Headless tests cover
pending duplicate disable, stale refresh, acknowledgement-loss/exception
uncertainty, capsule/expiry display, keyboard activation, automation names, and
unchanged `NOT SHARING`. Hosted runners and same-host evidence do not prove
physical networking, native restoration, crash/power-loss behavior, or a shipped
UI.

The target-local visible-undo slice reduces terminal protected Replace history
to unambiguous `workspace.note/v1` catalog frontiers on restart. Tests prove that
pending/recovering or structurally conflicting history produces no reconstructed
catalog and no action; only an exact unexpired, unconsumed frontier with no prior
undo attempt can be confirmed. View-model and Headless tests cover selection,
confirmation revocation, pending and terminal outcome text, keyboard operation,
accessible names, and persistent `NOT SHARING`. Restart replay must return the
recorded undo result with zero additional Adapter restore calls. This is semantic
note recovery from the protected descriptor, not native application or power-loss
evidence.

The restart reducer also rejects one-sided committed receipts/undos, orphaned or
mismatched capsules, and conflicting terminal transitions. A Desktop-service
preflight proves that direct callers cannot journal through a global unresolved
boundary or an unknown capsule while preserving the core endpoint's precise
expired, consumed, and revision-conflict outcomes. Recovery and confirmation
canaries prove that descriptor titles/payloads/digests and exception text do not
enter the new visible undo strings.

The task 8.1 Group/Scene model preserves a defensive immutable copy of 1 through
64 unique Activity IDs in exact caller order. Group and Scene revisions advance
with checked arithmetic; Group-derived Scenes bind the exact Group ID/revision
and require the explicitly expanded Scene items to match membership order.
Scene format v1 freezes typed Preserve/Move and Require Empty/Replace With Undo
policies without inventing another transfer primitive. Its 32 KiB/depth-8 JSON
codec has a golden 583-byte fixture and digest, emits canonical property/token
order, and rejects unknown, duplicate, missing, mistyped, non-canonical ID,
invalid revision/policy, over-bound, comment, trailing-comma, and trailing-data
inputs. Domain boundary tests also reject malformed UTF-16 in Group names,
Scene names, and Scene placement slots instead of allowing serializer
replacement characters to alter a definition. Canary fields named for payload,
traffic keys, and sessions are unknown schema and fail rather than round-trip.
These tests prove only the definition format; Scene repository, authorization,
preview/apply, per-Activity results, Replace confirmation/undo, UI, and
physical-device evidence remain open.

Task 8.2 Scene-apply tests treat saved order and uncertain-outcome halt as model
invariants. An expiring preview freezes exact Scene identity/revision/digest,
child IDs, explicit exact-source selections, current action/blocker evidence,
and exact destructive targets but grants no authority. Purpose-scoped exact-ID
source tests cover zero, one, and multiple active placements, mandatory explicit
selection plus complete repreview, exact-destination No Change, and permutation
tests proving discovery order, revision, title, or Device ID never selects a
source. Scene-specific exact-slot tests distinguish Empty, one Eligible
Conflict, Opaque protected/sensitive/restricted/different-kind/unsupported
occupancy, and Ambiguous, and prove a filtered empty Replace inventory cannot
authorize Empty. Closed policy-matrix tests prove Empty maps Preserve Source to
Handoff and Move After Acknowledgement to Move; one eligible conflict maps only
Preserve Source plus Replace With Undo to Replace; and occupied Move-plus-
Replace blocks with no operation, source cleanup, capsule, or compensation. The
operation port must independently recheck Trust, additional
`scene.apply`, operation-specific Offer/Receive or Receive/Replace Capabilities,
source state, exact-slot occupancy, connection, and Replace/undo evidence at
each use.
Table and seeded property tests cover all mixtures of committed, warning,
blocked, rejected, failed, Recovering, cancellation, thrown boundary failures,
and 1-through-64 item plans. Proven terminal outcomes continue; Recovering or
unknown outcome marks every remainder not attempted. Retry/restart tests prove
terminal replay without duplicate Adapter calls and a Started-without-terminal
record fails closed. Explicit compensation tests only exact committed Preserve-
Source Replace capsules in reverse order. Protocol 1.4 golden/hostile tests
cover strict source lookup, exact-slot, remote-child/result bindings and
1.0–1.3 unsupported paths.
A three-identity authenticated same-host integration proves a remote selected
source invokes the existing source-to-target operation, duplicate instructions
do not repeat Adapter work, uncertainty halts, and descriptor/payload canaries
never enter the coordinator. These portable contracts do not prove physical
two-device interruption, native application behavior, or native accessibility.

The tasks 6.1-6.2 portable Remote Window control tests exercise one public
`RemoteWindowSessionController` over deterministic authorization, capture,
input, and local-session boundaries. Tracers cover fresh-safe capture admission,
single-snapshot `mirror.view`/`mirror.drive` use, view-only admission, Driver
transfer, drive downgrade, view removal, disconnect, lease expiry, bounded
portable input, protection pause/resume, ordinary stop, emergency stop/reset,
and disposal. Fault cases inject start/input/pause/resume/stop exceptions and
cancellation while proving no exception or input payload reaches results. A
cancellation-ignoring capture double returns success after cancellation; the
controller rechecks the token, synchronously stops the capture boundary, and
propagates cancellation rather than publishing the session. Concurrency cases
block capture start or input and prove protection/emergency preemption, late-success
rejection, normal input/transfer serialization, and safe semaphore disposal.
Revocation removes the participant before local peer disconnect, retains an
unconfirmed disconnect as bounded pending cleanup, and retries without restoring
authority. Active participants and pending cleanup share the fixed 16-slot
budget; a re-authorized pending peer remains rejected until cleanup confirms.
Repeated and concurrent Emergency Stop attempts merge per-boundary confirmation
within the current stop/session generation, while a replacement session requires
fresh proof. Sixteen fixed seeds execute 48 authorization, role, transfer,
expiry, protection, and disconnect transitions each; after every event all
retired Device/epoch pairs are attempted through the public input API and must
not reach the input boundary.

Native Remote Window task 3 tests compose one connection-owned dispatcher over
the production authenticated TCP registration. Protocol 1.0-1.4 cases retain
their Activity routes without exposing a Remote Window channel or picker;
protocol 1.5 carries Activity and Remote Window traffic through the same strict
read loop. Real loopback tests cover coexistence, current Trust/Capability
refiltering, malformed cross-routing, reconnect, revoke, disposal, and complete
route-change notification drain. A raw authenticated peer receives concurrent
Replace, Swap snapshot, Scene source lookup, and Remote Window admission
requests, then responds in reverse order to prove cross-family correlation
without adding a competing reader. Deterministic session tests additionally
cover the 16-command pending bound, send/stop admission and in-flight drain,
throwing observer isolation, cancellation callback aggregation, re-entrant
disposal, stale copied execution context, and no pre-read past a blocked route.
These tests prove portable authenticated control composition on loopback only;
they do not route Remote Window media or prove native windows, physical Devices,
operating-system permissions, input injection, or protected-surface detection.

Native Remote Window task 4 freezes the protocol-1.6 media attachment separately
from that production listener composition. Codec tests pin the canonical 200-byte
`FSM1` request and 232-byte acknowledgement, require zero flags and exact lengths,
and bind the negotiated protocol, directed Device pair, 16-byte route locator,
Remote Window Session, Activity, and 32-byte initiator/responder nonces inside the
AEAD-protected body. Protocol 1.5 cannot construct or accept a media route. Hostile
tests cover truncated/trailing/tampered envelopes, clear/protected version or
route disagreement, wrong direction/Device/Session/Activity, a forged or wrongly
bound acknowledgement, and identifier-free diagnostics. A committed fixture
freezes both attachment envelopes while the existing fixed encrypted-media frame
codec vector remains unchanged.

Deterministic registry tests enforce a 32-route default and 128-route hard cap, a
30-second default and two-minute maximum TTL, separate 512-entry replay-nonce and
consumed-route-ID caps, one attachment per live control route, and a two-second
default/ten-second maximum handshake timeout. Tests fill each history, require
fail-closed admission at capacity, advance a manual clock to prove bounded
recovery, retain an attached route's history slot past the replay window, roll
back only a failed timer-arm admission, and forbid republishing a consumed ID
during cleanup or afterward inside the replay window. They also cover expiry,
repeated nonces across routes, a second claim, cancellation and timeout during
acknowledgement,
registration/disposal and revoke/claim races, replacement-route isolation,
observable timer cleanup failure, multi-owner cleanup joining, and
primary-plus-cleanup exception preservation. The clear locator is exercised only
as lookup input: matched malformed or failed claims consume that single-use
route, while possession never admits Capability, Driver authority, or plaintext.

One same-process loopback integration runs the real authenticated TCP handshake
at protocol 1.6 and proves both peers transfer their matching purpose-separated
media session exactly once. It then manually composes a registry and second
loopback listener, completes `FSM1` Connect/Accept, and exchanges one synthetic
bound encrypted media frame. This joins production cryptographic derivation to
the attachment contract, but it is not a production vertical or end-to-end test.
Attachment-wire write/read tests cancel streams that ignore cancellation and
prove borrowed envelope buffers remain stable and are not zeroed or returned
until the underlying I/O actually completes. The encrypted media channel applies
the same lifetime rule to a cancelled non-cooperative frame read.

Native Remote Window Task 5 Transport tests then use the production
`FlowspanTcpInboundListener` to classify `FSM1` beside pairing and authenticated
control with independent media capacity. A connection-owned media directory
transfers each protocol-1.6 media session exactly once, binds responder route or
initiator attachment to the control registration, and requests control stop on
route expiry/revoke, attachment failure, media fault, cancellation, or disposal.
Loopbacks cover authenticated attachment and a complete synthetic logical video
frame. Concurrency and fault tests cover claim/registration/disposal races,
non-cooperative handshake cancellation, handler-plus-cleanup failure preservation,
observer isolation, and listener shutdown without treating clear route possession
as authorization.

Task 5.4 adds internal shrink-only media usage limits. The injected limits must be
positive and cannot exceed the frozen production bounds, while every public
authenticated connection and listener path retains `2^20` protected frames and
1 GiB of plaintext per direction and epoch. A 2-by-2 same-host managed real-TCP
matrix uses frame limit 2 or plaintext limit 220 bytes in both initiator-to-
responder and responder-to-initiator directions. Each case admits the protected
attachment envelope and last legal media frame, rejects the next media send
before another wire byte is written, closes both attachment and owning control
registrations, empties both route registries and media directories, and removes
both Activity and Remote Window channels. Recovery completes a new authenticated
control handshake and derives a different media session and route. An old `FSM1`
request is rejected while the new route remains live and usable. A separate
handshake test rejects prior-session media ciphertext without advancing the fresh
receive epoch or sequence, then accepts ciphertext created by the fresh session.
The media epoch remains one throughout exhaustion; no test raises a production
budget, rekeys media in place, or republishes the consumed route.

Native Remote Window Task 5.5a freezes protocol-1.7 Preparation separately from
the still-open Desktop/native runtime. Protocol fixtures cover one canonical
host-to-participant Prepare, participant-to-host Ready success, and Ready
rejection. Each fixture repeats the exact correlation, Session, Activity,
directed Devices, role, deadline, and uppercase hexadecimal `prepareDigest`.
`Fixtures/remote-window-preparation-v1.7.json` freezes all three complete
canonical frames and their SHA-256 hashes. The matching codec test decodes each
fixture, re-encodes it byte-for-byte, and runs beside the unchanged protocol-1.5
control and protocol-1.6 `FSM1` fixture tests.
Digest vectors independently construct the UTF-8 newline-separated
`flowspan.remote-window.prepare.v1` domain, negotiated major/minor, correlation,
Session, Activity, host, participant, canonical role, and deadline Unix
milliseconds, then verify the SHA-256 bytes. Tamper cases change each component,
use malformed/short/long/cross-request digests, and require constant-time digest
comparison in the implementation. Every committed protocol-1.5 and 1.6 fixture
and hash remains unchanged; negotiated 1.6 rejects both new message types without
falling back to Admission, Activity transfer, state, or clear media.

Strict codec cases cover unknown, duplicate, missing, null, wrong-type, and
trailing fields; noncanonical UTC or envelope/body deadline disagreement; zero,
self, wrong, or swapped Device bindings; wrong authenticated sender/recipient;
wrong Session, Activity, role, correlation, and version; expired deadlines; and
allowlisted Ready reasons. A well-formed local rejection emits Ready false, while
malformed or wrongly bound traffic faults the control connection without a
reflected response. Fixtures and diagnostics use canaries to prove there is no
native token/handle/generation, route ID, Descriptor/Kind, raw title, key, input,
frame, or exception text.
The generic control decoder additionally requires the new Prepare/Ready outer
envelope to contain exactly `magic`, `protocol`, `type`, `messageId`,
`correlationId`, `senderDeviceId`, `sentAt`, `ttlMs`, `bodyDigest`, and `body`,
with exactly `major` and `minor` inside `protocol`. This closed outer schema is
limited to protocol-1.7 Preparation messages; explicit compatibility tests prove
that frozen protocol-1.5 and 1.6 readers still tolerate extension fields.
Prepare and Ready writer tests also feed system-like sub-millisecond timestamps,
then require one canonical whole-millisecond UTC `sentAt`, an exact integral TTL,
and unchanged deadline semantics. Lexical cases pin fixed-width date/time,
literal `+00:00`, omitted zero milliseconds, and shortest `.001`/`.01`/`.1`/
`.12`/`.123` fractions while rejecting `Z`, other offsets, redundant zeros, and
sub-millisecond values. Frozen 1.5/1.6 UTC readers remain compatible. Decoder
tests independently reject a supported but non-negotiated future protocol
version.

State-machine tests reserve at most one Preparation on each authenticated
registration and retain a terminal tombstone through its deadline or connection
close. They cover duplicate and conflicting Prepare, concurrent reservations,
unknown/cross-request/duplicate Ready, Ready after reject/timeout/cancel/revoke/
disconnect, a requested-role change, and a stale connection generation. Once
route-role selection occurs, every terminal failure must consume that media
session, close the owning control connection, and reject retry until a fresh
authenticated handshake supplies a new media session, route, Session ID, and
correlation.

Direction coverage at the trust-bound Preparation boundary must use
complementary one-way Trust grants. The source host's grant to the participant
must satisfy view-only or DriverEligible checks before Prepare, before capture,
and at AddParticipant. The receiving participant has no reciprocal Mirror grant
and must still prepare under current authenticated Trust and local receive
policy. Reversing the grant must not authorize the original source direction;
tests must not invent a `remote-window.receive` Capability. The current focused
codec/session filters prove directed Device bindings but do not yet include this
complementary-grant and reversed-grant-negative matrix.

Dispatch concurrency tests block media connect, renderer readiness, Ready send,
host Start, AddParticipant, and final state separately. `HandlePrepare` must
validate/reserve, launch one owned deadline/lifetime worker, and return so the
sole authenticated read loop continues routing unrelated control and stop
traffic. Stop/dispose cancels and joins the worker without callback self-wait.
On the host, a Ready exposed during Prepare send remains `ReadyBuffered`; final
Admission and caller completion require the same-lock `ReadyAcknowledged`
transition after the send succeeds. Tests prove a failed Prepare send cannot
leak final Admission, an acknowledged result cannot be reversed by Stop or a
later deadline read, and an expired Prepare, Ready, or final Admission cannot
enter the wire boundary even when its timer/watchdog has not run.
An Admission received before Ready send begins is rejected without invoking the
participant endpoint. During an exposed but incomplete Ready send, at most one
strictly bound Admission may be buffered; it is consumed only after send success
and is discarded on send failure. Final binding publication shares the Stop
linearization lock and rechecks cancellation, deadline, owner, and phase before
committing.
`AuthenticatedActivitySessionHandler.Changed` publishes synchronously after its
lifecycle lock is released. Observers may snapshot the now-Ready routes or start
nonblocking channel work, but must hand off rather than synchronously wait for a
round trip that requires the same dispatcher's sole read loop. Subscriber
exceptions are isolated; subscriber scheduling is not asynchronous. Tests prove
the first notification sees only started routes and a pre-cancelled run publishes
nothing, not that arbitrary observers are asynchronously isolated.

The Desktop network composition now gives the authenticated control handler and
published listener the same process-owned media directory. A Ready registration
also exposes an atomic generation-bound lease over its Preparation channel and
transferred media session. Generation revocation callbacks use a generation-owned
registration API and an invocation-local execution-context marker: callback-owned
direct or `Task.Run` disposal cannot self-wait, while a returned callback's copied
context cannot bypass cleanup even while a sibling callback remains active.
Registration/disposal races keep the cancellation source alive until cancellation,
registration establishment, and all leases have drained; a weak-reference test
proves completed generations are not retained in the caller's context.
Implementation commit `7255f04` adds a host/participant coordinator that uses
that lease with a verified peer-endpoint connector, prepares the host responder
route before Prepare, and completes participant initiator `FSM1` plus renderer
readiness before Ready. The shipped composition root still keeps Remote Window
unavailable while native boundaries are absent. Route locators must never appear
in control JSON.

Implementation commit `80191d6` adds an explicit Preparation response-completion
boundary. `CompletePreparationResponseAsync` runs exactly once after the
Ready/Rejected wire-send attempt, and its `responseCommitted` argument
distinguishes a committed response from a send that was not admitted or threw.
An `FSM1` attachment failure marks the participant connection generation
`failClosePending`; that generation immediately rejects retry, reacquisition,
peer-route operations, and media acquisition or use. After a committed Rejected
response, the participant completes local non-fail-close cleanup and the host
closes the owning connection after observing the result. If response delivery
did not commit, including a send throw, the participant fail-closes itself. A
committed inbound rejection retains the original Preparation deadline, providing
a bounded participant fail-close fallback if the host crashes or does not close
the connection. Tests also prove that response-delivery and completion-hook
failures are aggregated, and that an attachment primary failure remains
observable with cleanup failure even when Dispose or disconnect wins the
response-completion race.

Commit `ca63874` closes the remaining accepted-TCP attachment branch of that
ordering. When the peer stream connects but `FSM1` attachment or acknowledgement
then fails, the preparation-only media call leaves control ownership intact
while the connection lease marks the generation fail-close-pending. The bounded
Rejected response, its completion hook, explicit terminal cleanup, or the
original deadline then owns closure. Ordinary non-preparation media connects
retain eager control stop. A real loopback listener that accepts and immediately
resets the socket exercises this branch without depending on platform-specific
bound-but-not-listening TCP behavior.

The same commit hardens active host permission generations. An event with a
changed permission owner triggers current-snapshot revalidation; an authoritative
owner change invalidates the active generation, while a same-owner revision
watermark ignores stale lower revisions. Cleanup retires a generation's
callbacks before draining callbacks already admitted, so a captured callback
from a stopped generation cannot poison a replacement or `TerminalFailure`.
Callback-owned Stop fails promptly instead of waiting on its own lease;
callback-owned Dispose starts the shared full cleanup without self-wait, while
external callers still join and observe that same completion. A copied or stale
callback context loses that exemption after its callback token retires.

Implementation commit `7255f04`, as documented by evidence commit `81b9008`,
established a narrow production-composed managed two-node tracer over real
authenticated loopback TCP and the shared `FSM1` listener, with deterministic
host source/capture/input/protection/Emergency doubles and a participant renderer.
That historical evidence covers exactly four scenarios: a successful
DriverEligible capture/frame/input/Emergency Stop cleanup; reverse-only
Mirror-grant rejection; active authenticated control disconnect cleanup; and
same-session Mirror capability downgrade with active-session cleanup. The
success scenario asserts capture remains closed before Ready plus attachment
acknowledgement, orders current revalidation, safety registration, controller
Start, and exact AddParticipant with frame admission closed, then carries one
source frame through JPEG encode, encrypted chunking, decode, and renderer and
returns one authorized Driver input to the exact host boundary.

At implementation commit `80191d6`, retained by `761ac75`, that tracer checkpoint
keeps those four historical scenarios and adds managed native-capture permission
loss (`Granted` to `Denied`) plus verified `FSM1` attachment failure after a
proved TCP accept. It therefore covers exactly six scenarios: success,
reversed-grant rejection, authenticated-control disconnect, Mirror capability
revocation, managed native-capture permission loss, and `FSM1` attachment
failure. The attachment-failure tracer rejects before Admission, capture, media
send, or rendering, then clears route, media, control, protection,
permission-observer, and connection owners.

Subsequent checkpoint `fde38b2bae9d02f177fd86e22a8beecb060325e9`
adds three renderer-preparation cases after real authenticated protocol-1.7
control and successful `FSM1`, bringing the managed tracer to nine cases. The
new theory drives a renderer factory throw, a valid null/Missing result, and a
foreign or tokenless `OperationCanceledException`. Before injecting each
failure, the test independently observes both the host and participant media
sessions as attached and checks the exact protocol, Device pair, Session, and
Activity binding. The participant synchronously marks its generation
fail-close-pending before the response is observed. The host then observes
Rejected with `renderer_start_failed` for throw or foreign cancellation, or
`renderer_unavailable` for null/Missing, before the generation closes. Every
case asserts zero Admission, capture, media send, and render operations and zero
remaining owner, route, media-directory, and authenticated-control counts.

Only cancellation tied to the actual linked generation/caller token or the
Preparation deadline is eager cancellation. A foreign or tokenless
`OperationCanceledException` is a renderer-start fault and follows the bounded
response-before-close path. Its deferral is bound to the exact Preparation
request and a deadline no more than 10 seconds away. The watchdog survives
connection-lease disposal and closes the generation at the request deadline if
the host does not. Repeating the same request is idempotent; a conflicting
request cannot replace or extend it. Expired, overlong, conflicting, or
time-provider setup failure refuses deferral without poisoning the generation
and therefore uses eager fail-close. Explicit close and deadline expiry share
one cleanup, owner revocation cancels the watchdog, and tests retain primary
renderer failure together with cleanup and lifecycle failures.

Test-only checkpoint `0f1f32d0e8ea251194755a5b4d150d3e294433ff`
adds a tenth tracer case without changing production source. After real
authenticated protocol 1.7 with a signed, verified endpoint, successful `FSM1`
and Ready, and one renderer Prepare, the test independently observes both media
sessions as attached to the exact protocol, Device, Session, and Activity
binding. A coordinator-only `MutableClock` is then set exactly to the request
deadline. The existing production `EnsurePreparationIsCurrent` equality check
fails closed with allowlisted `preparation_expired`. `WaitForMediaAttachment`
runs once; Admission publication, capture, media send, and rendering remain at
zero. Host fail-close and Dispose each run once. Snapshot and TerminalFailure
are null; ActiveMediaBudget is null because no active generation was published,
not because this test observed a pending budget. Renderer, route, directory,
handler, lease, channel, and control ownership drain to zero, and the retired
generation cannot be reacquired.

The release criterion still requires each tracer boundary to have reject, throw,
cancel, timeout, revoke, disconnect, and cleanup-fault cases. In particular, the
current twenty-six scenarios are not the required matrix; its per-boundary
reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault coverage remains
open. Teardown requirements remain to close new admission first, attempt every
renderer, active/pending frame, queue, attachment, route, media-directory,
controller, protection, Emergency Stop, and control owner, and preserve combined
failures. Success and every fault must end with zero retained owner/budget
counts.

Exact-implementation local macOS verification at `761ac75` passed the complete
Debug and Release solutions at `2210/2210` in each configuration, including
Desktop `535/535` and Transport `688/688` in each. These are local managed,
loopback, and contract results. Exact-SHA hosted CI `33246518217` passes
`2210/2210` on Windows, macOS, and Linux, plus Secret Scan and three reproducible
unsigned package jobs; CodeQL `33246518202` also passes. Downloaded TRX and SARIF
artifacts confirm the counts and zero non-success/secret-scan results.

For subsequent exact implementation SHA `fde38b2`, local macOS arm64
verification with .NET SDK 10.0.301 passed warning-as-error Debug and Release
builds with zero warnings and errors. Both complete solutions passed
`2232/2232`; Desktop passed `544/544` and Transport passed `701/701` in each
configuration. Ten fresh Debug and ten fresh Release renderer-theory processes
passed `60/60` case executions. The focused connection-lease suite passed
`16/16` and the focused media-session suite passed `28/28` in each
configuration. Formatting, diff, direct/transitive NuGet vulnerability,
explicit TEST MODE composition, and deterministic simulator checks passed. This
host does not have `gitleaks`, so there is no local secret-scan result.
Exact-SHA CI `33249181870` and CodeQL `33249181871` for `fde38b2` both
succeeded. Downloaded Windows, macOS, and Linux artifacts each contain 12 TRX
files summing to `2232/2232`, with every non-success counter zero. Secret Scan
and all three reproducible unsigned package jobs also passed.

At test-only SHA `0f1f32d`, local macOS focused expiry runs passed `1/1` in
Debug and Release, and the complete managed tracer class passed `10/10` in both
configurations. Warning-as-error builds completed with zero warnings and errors;
the complete Debug and Release solutions each passed `2233/2233`, including
Desktop `545/545` and Transport `701/701`. Formatting, diff, direct/transitive
NuGet vulnerability, explicit TEST MODE composition, and deterministic simulator
checks passed. Internal strict review reported no P0, P1, or P2 finding, which is
not an external audit. Superseding SHA
`e504c839cac2e45a4ca7ad17316c8278e4928c2e` passed exact-SHA CI
`33250747660` and CodeQL `33250747671`: each hosted OS passed `2233/2233`, with
Secret Scan and all reproducible unsigned package jobs also passing.

A preceding docs-only CI run `33249644505` had one intermittent Windows testhost
stall: attempt 1 produced no Desktop TRX before the 20-minute job timeout/cancel,
while its other 11 TRX files passed `1688/1688`. Isolated Windows rerun job
`99095158216` then produced all 12 TRX files at `2232/2232` and made attempt 2
successful. This is evidence of an intermittent hang, not a deterministic
Windows platform failure. The superseding workflow adds `--blame-hang`, a
three-minute hang timeout, and no memory dump; its ordinary Windows test step
passed in 51 seconds. The guard only makes a future hang fail fast with sequence
diagnostics. It neither identifies nor fixes the unknown root cause and collects
no memory dump.

At the expiry checkpoint, this addition covered one post-`FSM1`, pre-Admission
deadline-equality timeout. Actual caller cancellation, cleanup-fault injection,
and the complete per-boundary matrix remained open.

Test commits `45e2d494501167712ec4abdff69d8d232f355d14` and
`5bb6d0863033c3b6668335e15d6a6fe336ee46a7` add an eleventh managed tracer case
without production source changes. After real authenticated protocol 1.7,
signed candidate verification, successful `FSM1` and Ready, and exact bilateral
attachment, an independent caller CTS supplied only to `StartAsync` is cancelled
by the final hook while the harness CTS continues to own connection, run, and
cleanup and the clock remains strictly before the deadline. Production surfaces
the cancellation family—observed as `TaskCanceledException`—with the exact
caller token. It is therefore neither timeout nor a foreign renderer fault, and
no rejection reason is produced. Admission, capture, media send, and rendering
remain zero; host fail-close and Dispose each run once and all owners drain.

Focused caller cancellation passed `1/1` in Debug and Release, the tracer class
passed `11/11` in each, and twenty fresh Debug processes passed the caller case
`20/20`. After the fixture reliability repair, both warning-as-error builds
passed and the Debug and Release solutions each passed `2234/2234`, including
Desktop `546/546`, Platform `219/219`, and Transport `701/701`. Formatting,
diff, dependency-vulnerability, explicit TEST MODE composition, and simulator
checks passed. Strict caller review reported no P0/P1/P2 finding. Exact-SHA
CI `33251741558` and CodeQL `33251741546` for `5bb6d08` both succeeded. Each
hosted OS passed `2234/2234` with every non-success TRX counter zero; Secret Scan
and all three reproducible unsigned package jobs also passed.

The first full Debug run exposed an old Platform-test fixture race: expected
`BoundaryFailed`, actual `Applied`. The production state lock was already the
correct linearization boundary; only the fake `RecordingCaptureBoundary` shared
a non-atomic call count. Before repair, parallel stress failed 23 of 400 runs.
The fixture now uses an interlocked capture count, deterministic barrier, locked
timeline, and `finally` release/join. Post-repair stress passed `160/160` plus
`80/80`, and strict review reported no P0/P1/P2 finding. This is a test-fixture
reliability repair, not a product defect.

Docs-only SHA `f300432c7e372658f06d2196a182c3c9ddfc99af` then exposed a
second test-fixture scheduling dependency in CI `33252295470`. Linux and macOS
each passed `2234/2234`, but the Windows Desktop TRX passed `545/546`: only
`ExactParticipantPeerDisconnectRoutesAndDrainsBeforeReplacement` failed after
five seconds because it still observed the old current generation. Production
`Register` retires that generation synchronously before waiting for its routed
call to drain. The test, however, placed a synchronously blocked peer disconnect
and the synchronously draining replacement `Register` on the shared thread
pool, then used a tight `Task.Yield` polling loop. It could therefore report
"not retired" before the replacement delegate had started under full-suite
Windows scheduling pressure. No production state-machine defect was observed.
CodeQL `33252295459` and Secret Scan succeeded, but the failed CI and skipped
package jobs are not acceptance evidence.

Test-only reliability commit `7b6a6d6796e0280c53eb71755285090c8e19cb5d`
moves every synchronously blocking host-control disconnect, replacement
`Register`, and external registration `Dispose` in that test class onto a
dedicated `LongRunning` task. The failing case also waits for an explicit
replacement-start gate before checking retirement, and its bounded poll uses a
10 ms cancellable delay rather than a tight yield. The assertions still fail if
production does not retire current, publishes the replacement before drain, or
completes either lifetime operation early.

Local Debug and Release warning-as-error builds completed with zero warnings and
errors; both solutions passed `2234/2234`, including Desktop `546/546`. The
focused class passed `15/15` in each configuration. With a small but runnable
worker limit, the exact case passed in two seconds; eight concurrent processes
then completed 80 class runs, or `1200/1200` case executions, in 28 seconds.
A maximum of two workers also starved vstest/xUnit continuations after the code
change, so that artificial runtime setting is diagnostic evidence only and is
not counted as a passing regression gate. Format, diff, direct/transitive NuGet
vulnerability, explicit TEST MODE composition, and simulator checks passed.
Strict review found no P0/P1 in the change. It retained one existing P2
test-only cleanup debt: several sibling failure paths do not yet release their
blocking fake in `finally`, which can compound diagnostics if a future
production regression triggers those assertions.

Exact-SHA CI `33253258876` and CodeQL `33253258929` for `7b6a6d6` both
succeeded. Downloaded Windows, Linux, and macOS artifacts each contain 12 TRX
files at `2234/2234`, with every failed, error, timeout, and aborted counter
zero. Secret Scan, CodeQL analysis, and all three reproducible unsigned package
jobs also passed. This closes the recorded Windows test-scheduling failure; it
does not add product behavior or native/physical evidence.

Docs SHA `908a04a2f465bccccf56b72fd36cb5f048506a63` then exposed a
different renderer-tracer sampling race in Linux CI `33254082958`. Windows and
macOS passed `2234/2234`; Linux passed `2233/2234`, with only the renderer
`Throw` row failing because the factory sampled host `IsAttached == false`.
Responder FSM1 legitimately writes its acknowledgement before it commits and
publishes the host directory attachment, while the initiator may enter renderer
preparation immediately after validating that acknowledgement. CodeQL and
Secret Scan passed, but packages were skipped; the failed CI is diagnostic, not
acceptance evidence.

Test-only commit `ac48ec3aa88aa78f736b5550bc778a5ff4e95abb` makes the
advertised bilateral-attachment boundary deterministic. The renderer fixture
awaits both real media-session attachment completions with the bounded
generation token, records an explicit completed barrier, and only then injects
throw, Missing, or foreign cancellation. This is test-owned synchronization,
not a new production happens-before rule. Debug and Release solutions passed
`2234/2234`; the focused theory passed 120 case executions across 40 fresh,
eight-way concurrent processes; and strict review found no P0/P1/P2. Exact-SHA
CI `33254883850` and CodeQL `33254883851` succeeded, with each hosted OS at
`2234/2234`, Secret Scan and all three unsigned package jobs passing. Immediate
renderer failure after initiator acknowledgement but before host directory
publication was the next separate fault-matrix row.

Test-only commit `58569be3215bbb38a6767398d28c3f428130601a` closes that one
row without changing production source. A wrapper around the real listener media
handler blocks after authenticated FSM1 acknowledgement and route attachment but
before forwarding to the host directory. The participant session is attached;
the exact host session and binding are visible but unattached. Renderer failure
then produces a real Rejected response while the host wrapper still reports zero
fail-close/Dispose. The test releases the media gate, waits for real host
attachment, and only then returns the Rejected response; all owners converge to
zero with no Admission, capture, send, or render. The TDD RED timed out only the
new row at 258 ms while the other three passed; the final GREEN theory passed
`160/160` under eight-way fresh-process pressure. Debug and Release solutions
passed `2235/2235`, including Desktop `547/547`, and strict review found no
P0/P1/P2.

Exact-SHA CI `33256672974` and CodeQL `33256672962` succeeded. Every hosted OS
passed `2235/2235`; Secret Scan and all three reproducible unsigned package jobs
passed. This closes only the pre-directory renderer-failure row. The complete
per-boundary reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault matrix
remains open.

Test-only commit `63a52e5e7d2cbba7555a084bc6fa389dba6b5dd9` adds a fifth
renderer row and thirteenth managed tracer case without changing production
source. It holds the real listener before host directory publication but lets
the real Rejected response return. The test requires Start, fail-close, Dispose,
and the coordinator/control/directory/route/lease cleanup to complete while one
listener handler remains deliberately blocked with `ForwardCount == 0`;
Admission, capture, send, and render stay zero. Only then is the gate released.
The delayed attachment must fail at `MediaAttachment` with
the expected stale-owner `InvalidDataException`, and a second cleanup check must
show no owner or route resurrection. It deliberately does not create a
replacement generation and therefore is not an ABA case.

The TDD RED added only the new row/boundary value; four rows passed and the new
row failed after 29 ms after sampling an already-attached host session. GREEN
reused the media gate and added boundary-specific cleanup-before-release
orchestration. The final focused theory passed `5/5` in Debug and Release, the
tracer class passed `13/13` in both, and 40 fresh
processes at eight-way concurrency passed all five rows for `200/200`. Both full
warning-as-error builds completed with zero warnings/errors; both full solutions
passed `2236/2236`, including Desktop `548/548`, Platform `219/219`, and
Transport `701/701`. Strict review found no P0/P1/P2. This is test capability,
not evidence of a production defect or code change.

That full validation found two ordering gaps in
`DuplicateLocalRekeyRequestsCoalesceAtOneTargetEpoch`. Responder
`SendEpoch == 1` was a post-flush/pre-local-advance sample; initiator
`SendEpoch == 3` proved the second call could start after the first completed.
Test-only commit `0e573907c30cf34b97339a1dd79ee8d3ca824399` starts both calls
before server receive, then uses a marker returned by that receive loop as the
responder-transition barrier. The production send gate already prevents an
old-epoch application-frame interleave, so no production source changes. Two
hundred fresh alternating Debug/Release processes passed the repaired case.

Exact HEAD CI
[`33259599324`](https://github.com/happys2333/flowspan/actions/runs/33259599324)
and CodeQL
[`33259599282`](https://github.com/happys2333/flowspan/actions/runs/33259599282)
completed successfully. Each hosted OS passed `2236/2236` with every non-success
counter zero; Secret Scan, CodeQL analysis, and all three reproducible unsigned
package jobs passed. The new renderer row closes only that one cleanup-race
boundary. The remaining replacement/ABA matrix, the remaining complete fault
matrix, Tasks 5, 5.5a, and 5.5, all native/physical/release gates, and the Goal
remain open;
`CreateProduction()` remains unavailable.

Test-only commit `ba58562aff020e3cd9fcc5c8066bcfe74d692b8b` adds one
independent Transport ABA contract without changing production source or the
thirteen-case managed Desktop tracer. The fact must use two independently
releasable gates after real route acceptance and before authenticated-directory
publication. It first drains the old control generation, then proves a higher
replacement generation for the same Device pair has an Attached route for the
same Session and Activity with a distinct Route ID. Releasing only the old gate
must yield the expected stale-owner `InvalidDataException` while the replacement
remains current, unattached in the host directory, not stopped, and owns exactly
one route. Releasing the new gate must attach only the replacement binding,
transfer one encrypted frame, and permit complete final cleanup.

One deliberately shared gate failed the fixture capability check with expected
forward count 1 and actual 2 after about 62 ms. The correct two-gate test was
GREEN against current production. Removing only the exact-binding inequality
guard then made the bounded test fail after about five seconds because the old
attachment polluted the replacement; restoring the guard returned it to GREEN.
Final focused Debug/Release passed `1/1`, the class passed `29/29`, 80 fresh
Debug processes at eight-way concurrency passed `80/80`, Transport Debug/Release
passed `702/702`, and both complete solutions passed `2237/2237`. Both warning-
as-error builds, format, diff, dependency-vulnerability, TEST MODE composition,
and simulator gates passed.

Exact-SHA CI
[`33261748925`](https://github.com/happys2333/flowspan/actions/runs/33261748925)
attempt 1 retained a macOS exit-137 runner failure during format, before build,
test, or TRX. Attempt 2 reran the unchanged SHA successfully: all three hosted OS
artifacts contained 12 TRX files and `2237/2237` passing tests; Secret Scan and
all reproducible unsigned package jobs passed. CodeQL
[`33261748927`](https://github.com/happys2333/flowspan/actions/runs/33261748927)
evaluated 52 rules with 0 results and 0 open alerts. Exact job, artifact, and
digest records are in the Transport candidate evidence.

At this `ba58562` checkpoint, this closed only the old-exact-binding-versus-
prepared-replacement row. Required coverage still included a full Desktop
renderer-to-replacement trace; the other
Session, Activity, Device, reconnect, reject, throw, cancellation, timeout,
revocation, disconnect, and cleanup-fault combinations; their combined-failure
cross-products; and native/physical execution. Tests must not infer any of those
from this isolated row.

Documentation SHA `124b1a0c8325d7b469702682f8b7f14c1aebfa54` exposed a
renderer-rejection fixture race in macOS CI `33262767594`. Verifying the
initiator's real `FSM1` acknowledgement does not prove that the responder has
returned from `AcceptAsync` and published the attachment into the host directory.
Immediate response cleanup can legally close the stream inside that interval.

Test-only commit `5e5f380393a46021d8106a7f3fa817d3b7ac3765` therefore
requires every fixture that claims a post-attachment renderer rejection/cleanup
boundary to first assert the exact Rejected outcome and reason, then wait with a
five-second cancellable token for responder host-directory attachment
publication. The response assertions must precede the barrier so an earlier
rejection regression is not hidden behind an attachment timeout. A temporary
100-ms delay after acknowledgement write made the hosted exception deterministic;
the corrected fixtures passed under that probe, after which the instrumentation
was removed and production source remained unchanged. Final class Debug/Release
passed `17/17`, 40 fresh alternating processes passed `680/680`, Desktop passed
`548/548`, and both solutions passed `2237/2237`.

Exact-SHA CI `33263840825` passed `2237/2237` on each hosted OS, Secret
Scan, and all three reproducible unsigned package jobs; CodeQL `33263840823`
passed 52 rules with 0 results and 0 open alerts. This is a test-owned
synchronization requirement, not a production acknowledgement-to-publication
happens-before guarantee and not a new tracer case.

Test-only commit `8841080d8cfbfa3714b3cb7c6d858396ceb756b8` adds the
fourteenth managed tracer case without changing production source. After real
authenticated protocol 1.7, `FSM1`, Ready, Admission, capture, encrypted media,
decode, and one render, participant authenticated-control disconnect starts
terminal cleanup. The injected Emergency Stop registration `Dispose` first
clears its callback and becomes non-current, then throws one `IOException`.
Tests must prove that exact exception instance remains observable through
`TerminalFailure` and coordinator Dispose, while capture/input Emergency Stop,
sharing disconnect, capture, renderer, protection, permission observer, budget,
both media directories/routes, both handlers/channels, host connection, and
current/retained control generation all drain.

The fixture-capability RED failed before its registrar could inject or count the
fault. The minimal GREEN changed only that test seam. Final focused Debug/Release
passed `1/1`, 80 fresh alternating processes passed `80/80`, the tracer passed
`14/14`, Desktop passed `549/549`, and both solutions passed `2238/2238`.
Exact-SHA CI `33264566458` passed `2238/2238` on every hosted OS plus Secret
Scan and all three reproducible unsigned package jobs; CodeQL `33264566368`
passed 52 rules with 0 results and 0 open alerts. Exact artifacts are recorded in
the managed tracer evidence.

This closes one active authenticated-disconnect by Emergency Stop registration-
disposal cleanup-fault intersection only. Other cleanup owners, combined cleanup
failures, and the remaining per-boundary matrix stay open.

Test-only commit `6ff3fefaa667e23f309681fe5fe953ae97bb5861` adds the
fifteenth managed tracer case,
`RendererFailureLateAttachmentCannotRetargetReplacementDesktopGeneration`, and
must preserve one causal two-generation trace rather than infer composition from
separate tests. Generation 1 completes real authenticated TCP, protocol 1.7, and
`FSM1`; its accepted route is Attached while an independent gate blocks host-
directory publication. Renderer Prepare throws, Rejected is observed before
fail-close, and the old coordinator/control/directory/route/lease graph drains
while the old listener handler remains blocked. The same Device pair then
reconnects with strictly higher host and participant control generations and
fresh Session, Correlation, and Route IDs. Generation 2 independently reaches
Attached-before-publication. Releasing only the old gate must reject the stale
exact binding with the no-live-owner failure while generation 2 remains current,
unstopped, host-unattached, and pre-Admission with exactly one route and zero
capture, media send, render, or retained driving/controller generation. Releasing
generation 2's
gate must then produce Applied Admission and transfer one BGRA frame through
JPEG, authenticated encryption, decode, and render before Stop and full owner
drain.

The fixture must keep both publication gates independent. A deliberately shared-
gate RED failed after 442 ms because releasing the old generation also released
the replacement. Temporarily removing the production exact-binding inequality
guard made the bounded focused test time out after 30 seconds because the stale
attachment occupied the replacement; the guard was immediately restored. Release-
class validation exposed the media-attachment handler's Completion-to-Exited
publication gap. Later fresh-process pressure exposed that public participant
connection cleanup could precede renderer-disposal visibility. Explicit bounded
barriers now observe both terminal publications without changing production
source. The final focused fact
passed `1/1` in Debug and Release; the tracer passed `15/15` in each; 160 fresh
alternating Debug/Release processes passed `160/160`; Desktop passed `550/550`;
and both complete solutions passed `2239/2239`. Both warning-as-error builds
completed with zero warnings and errors, and format, diff, direct/transitive
dependency vulnerability, TEST MODE composition, and simulator gates passed.
Exact-SHA CI `33266348260` passed on macOS, Linux, and Windows; each downloaded
12-file TRX artifact sums to `2239/2239` with every non-success counter zero.
Secret Scan passed 208 rules with 0 results, all three reproducible unsigned
package jobs passed, and CodeQL `33266348243` passed 52 rules with 0 results and
0 exact-commit open alerts. Exact artifact IDs and digests are in the managed
tracer evidence.

This closes one full managed renderer-failure-to-replacement exact-binding trace,
not the replacement matrix. Other Session, Activity, Device, reconnect, boundary-
failure, cleanup-fault, and combined-failure variants remain open.

Test-only commit `13681fb451df53290496416d11837ffb5435e500` adds the
sixteenth managed tracer case without changing production source. The active
authenticated-disconnect cleanup test is now a two-row theory: the historical
Emergency Stop registration-disposal fault and a capture Emergency Stop fault.
After real protocol 1.7, `FSM1`, Admission, encrypted media, and render, the
capture boundary clears its current owner and throws one injected `IOException`.
Production must project that injected managed capture-boundary exception only as
the stable
`capture=local_boundary_exception` reason, keep the projected terminal failure's
`InnerException` empty, and omit the injected message from its complete string.
The same Emergency Stop attempt must still confirm input stop and all-session
disconnect. Later cleanup calls ordinary capture and input Stop exactly once,
drains every renderer, protection, permission, budget, media, connection, and
control owner, and makes `TerminalFailure` and the first explicitly observed
coordinator `DisposeAsync` share the same projected failure instance.

The first RED left the new seam non-throwing: the existing row passed and the new
row hit its 20-second bound. The first one-shot throw then correctly exposed that
raw exception identity was the wrong public expectation; production already
performed bounded projection, so the test was refined to the T10-visible
contract. Final focused Debug/Release passed `2/2`; 80 fresh alternating
processes exercised both rows at `160/160`; the tracer passed `16/16`; Desktop
passed `551/551`; and both complete solutions passed `2240/2240`. Warning-as-
error builds, format, diff, dependency vulnerability, TEST MODE composition, and
simulator gates passed. Exact-SHA CI `33267557804` passed `2240/2240` on each
hosted OS, Secret Scan 208 rules with 0 results, and all three unsigned package
jobs. CodeQL `33267557806` passed 52 rules with 0 results and 0 exact-commit open
alerts. Exact artifacts and digests are in the managed tracer evidence. Final
strict review reported P0/P1/P2 at zero.

At the `13681fb` checkpoint, this closed one additional disconnect-by-capture-
cleanup-fault intersection only. Other cleanup owners, combined failures, and the
per-boundary matrix remained open.

Test-only commit `2c6ff3221c494cd7003ad0a55e91c28e473615da` adds the
seventeenth managed tracer case and no production source change. The third
authenticated-disconnect theory row combines the existing capture Emergency Stop
fault with the Emergency Stop registration-disposal fault in one cleanup. The
final terminal failure must be one outer `AggregateException` with exactly two
direct inner exceptions in causal order: the bounded, canary-free capture
projection first and the exact raw registration `IOException` instance second.
The first explicitly observed coordinator `DisposeAsync` must throw that same
outer instance. A test-side bounded wait avoids sampling an earlier one-failure
snapshot by waiting for final aggregate publication before assertions; every
boundary count and owner-drain assertion from the single capture row remains
exact.

The TDD RED intentionally omitted the combined value from only the registration
injection predicate. The two historical rows passed while the combined row alone
hit its 20-second final-aggregate bound (`2/3`). Extending that predicate was the
only GREEN change. Final focused Debug/Release passed `3/3`; 80 fresh alternating
processes exercised all rows at `240/240`; the tracer passed `17/17`; Desktop
passed `552/552`; and both solutions passed `2241/2241`. Warning-as-error builds,
format, diff, dependency vulnerability, TEST MODE composition, and simulator
gates passed. Exact-SHA CI `33269125217` passed `2241/2241` on each hosted OS,
Secret Scan 208 rules with 0 results, and all unsigned package jobs. CodeQL
`33269125313` passed 52 rules with 0 results and 0 exact-commit open alerts.
Exact artifacts and digests are in the managed tracer evidence. Final strict
review reported P0/P1/P2 at zero.

This closes one combined capture-plus-registration cleanup-fault cross-product
only. The other owner combinations and complete per-boundary matrix remain open.

Test-only commit `26cd380091f6fd387173e2565023cbb27a96aab0` adds two more
single-owner rows and no production source change. The eighteenth row injects a
one-shot input `EmergencyStopNow` exception only after its managed boundary has
applied the local stop. The required terminal surface is the exact bounded
`input=local_boundary_exception` projection with confirmed capture/session
reasons, no inner exception, and no input canary. The nineteenth row awaits the
real authenticated host connection's inner `DisposeAsync`, proves the lease is
non-current, and only then throws its one-shot cleanup exception. That exact raw
exception instance must be shared by `TerminalFailure` and the first explicitly
observed coordinator `DisposeAsync`.

The input RED left only its injection disconnected, so the historical three rows
passed and the new row alone reached its 20-second bound (`3/4`); GREEN passed
Debug/Release `4/4`. The connection RED similarly produced `4/5`; connecting
only the after-inner-dispose seam made Debug/Release `5/5`. Strict review found
that an ordinary Stop could make the first input proof appear green, so the
fixture now records an applied-before-failure event only inside the injected
Emergency Stop branch. Final review reported no P0/P1/P2 finding.

Forty fresh alternating processes passed all five theory rows at `200/200`; the
tracer passed `19/19`; Desktop passed `554/554`; and both Debug and Release
solutions passed `2243/2243`. Both warning-as-error builds and the format, diff,
dependency-vulnerability, TEST MODE composition, and simulator gates passed.
Exact-SHA CI `33270854982` passed `2243/2243` on each hosted OS, Secret Scan 208
rules with 0 results, and all three unsigned package jobs. CodeQL `33270854935`
passed 52 rules with 0 results and 0 exact-commit open alerts. Exact artifacts
and digests are in the managed tracer evidence.

These additions close one input cleanup owner and one late host-connection
disposal owner only. Other owners and their combined-failure cross-products
remain open.

Test-only commit `5c50870ee11639ee642781e647b135fdd4fc59f7` adds two more rows
and no production source change. The twentieth row injects host fail-close only
after awaiting the real inner fail-close. It must prove the immediate terminal
path and CleanupCore reuse one shared completion: one fail-close call, one
failure, the exact raw `IOException` through `TerminalFailure` and the first
explicitly observed coordinator `DisposeAsync`, followed by successful host-
connection disposal and complete owner drain. The twenty-first row injects the
existing Emergency Stop registration disposal and host-connection disposal seams
in one cleanup. Its final failure must be one flat `AggregateException` with
exactly two direct inners in causal cleanup order: the registration exception,
then the connection-disposal exception, both by identity.

The fail-close RED left only its after-inner seam disconnected, so the previous
five rows passed and that row alone reached the 20-second bound (`5/6`). GREEN
passed Debug/Release `6/6`. The combination RED intentionally injected only the
registration fault and failed quickly at `6/7` because production exposed that
single `IOException` instead of the expected aggregate; adding only the existing
connection-disposal predicate made Debug/Release `7/7`. Strict review reported
no P0/P1/P2 finding.

Forty fresh alternating processes passed all seven theory rows at `280/280`; the
tracer passed `21/21`; Desktop passed `556/556`; and both Debug and Release
solutions passed `2245/2245`. Both warning-as-error builds and the format, diff,
dependency-vulnerability, TEST MODE composition, and simulator gates passed.
Exact-SHA CI `33271787570` passed `2245/2245` on each hosted OS, Secret Scan 208
rules with 0 results, and all three unsigned package jobs. CodeQL `33271787616`
passed 52 rules with 0 results and 0 exact-commit open alerts. Exact artifacts
and digests are in the managed tracer evidence.

These additions close one host fail-close owner and one registration-plus-
connection-disposal cross-product only. Other owners and combinations remain
open.

The caller-cancellation tracer case covers only one post-`FSM1`, pre-Admission
actual caller cancellation. Five single-owner disconnect cleanup-fault
intersections and two combined-owner cross-products are now covered, together
with one full renderer-to-replacement exact-binding trace; the remaining cleanup-
fault injection, replacement/ABA variants, and complete per-boundary matrix
remain open.

The hosted matrices are cross-platform managed contract evidence,
not evidence for native platform APIs, two physical devices, accessibility,
interactive quality, package signing, or macOS notarization.
`CreateProduction()` must keep Remote Window unavailable until the native and
authenticated runtime is composed into it. The tracer does not close Task 5,
Task 5.5a, Task 5.5, any native/physical/release gate, release criterion, or the
long-term Goal.

### 2026-08-30 pre-Prepare safety candidate

The finite production-boundary inventory is maintained in
[`remote-window-production-boundary-matrix.md`](remote-window-production-boundary-matrix.md).
The current worktree candidate advances only its H0/H1 pre-Prepare rows. The
required order is: initial source/connection/permission/grant facts; one fresh,
exact-source `Safe` protection observation; host-fact revalidation; a pure,
prompt-free Emergency Stop readiness check; a second host-fact revalidation;
caller-cancellation and canonical-deadline barriers; then responder route
selection and Prepare.

Deterministic tests inject caller cancellation, deadline equality, source
invalidation, permission/grant revocation, and connection revocation at those
synchronous seams. They require route selection, Prepare, capture, controller,
participant, and Admission authority to remain closed. Non-fatal exceptions
from permission, authenticated-connection, protection, and readiness reads must
project respectively as `native_permission_unavailable`,
`authenticated_connection_stale`, `native_protection_not_safe`, and
`emergency_stop_readiness_unavailable`, without canary, native exception text,
or inner exceptions. `OutOfMemoryException` is not converted to a product
rejection. A pre-route safety callback that already started fail-close must be
joined by cleanup even when route selection never occurred; blocked and failing
test doubles freeze that completion and ordered failure identity.

These tests prove the named callback order, not absolute TOCTOU linearization
against an arbitrary concurrent thread. Readiness is observational and does not
reserve the later Emergency Stop registration; an atomic readiness-to-
registration reservation remains required before Task 5.5a can close.

The accompanying macOS adapter candidate calls
`CGPreflightScreenCaptureAccess` for prompt-free facts and crosses
`CGRequestScreenCaptureAccess` only from an explicit request. Input remains
`Unsupported`. Operation-sequenced commits discard late concurrent native
results, revisions advance only on changed facts, observers are isolated and
invoked outside the state lock, and disposal rejects late publication and new
native calls. The matching-host smoke is preflight only and does not prove
capture, input, protection, physical two-Device operation, packaged TCC,
signing, or notarization. The adapter is not wired into `CreateProduction()`.

Local Debug/Release solution verification passes `2286/2286` tests with zero
build warnings/errors. Exact-SHA CI `33275235290` and CodeQL `33275235305` pass
on evidence commit `92edfff`; candidate scope, commands, artifact digests, and
limitations are in
[`2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md`](../evidence/2026-08-30-pre-prepare-safety-and-macos-permission-preflight.md).
Tasks 5, 5.5a, and 5.5 and every native, physical, and release gate remain open.

### 2026-08-30 participant policy and final Admission faults

The P0 receive-policy contract now tests bounded valid rejection, unknown reason
reduction, and unexpected-throw redaction before any connection, renderer, Ready,
Admission, or render authority. A recovered policy must use a fresh request and
complete real loopback `FSM1` preparation before the tested owners drain.

At final Admission, the Host coordinator projects unexpected and foreign-token
publication failures to `host_admission_publish_failed`; only an OCE carrying the
exact caller token is propagated. The production authenticated lease separately
normalizes its linked cancellation back to the original caller token and proves
that a foreign token cannot be relabelled during a caller-cancellation race.

The 22nd production-composed tracer case waits for the participant endpoint to
commit its known binding and publish `StateChanged`, then injects a host wrapper
failure after the Admission wire side effect. Frame admission remains closed,
media/render remain zero, and the directly asserted owners across both nodes
drain before the old authenticated generation is rejected.

Focused Desktop host/participant/tracer tests pass `81/81` and focused lease
tests pass `18/18` in Debug and Release. Desktop passes `581/581`, Transport
passes `704/704`, and both complete solutions pass `2295/2295`; warning-as-error
builds, format, diff, vulnerability, composition, simulator, and final strict
review pass. Exact-SHA CI `33277518618` and CodeQL `33277518619` pass on evidence
commit `158c9a1`; downloaded artifacts prove `2295/2295` on each hosted OS,
Gitleaks 208/0, and CodeQL 52/0. P0, AD, and HC stay partial in the finite matrix,
so Task 5.5a remains open.

### 2026-08-30 Host Preparation reservation core

Commit `294042fdfcc346e3eade3551d57cc7ccba95c601` adds one internal
`Flowspan.Desktop` state-machine core for the host fact reservation frozen by
[ADR 0027](../adr/0027-remote-window-host-preparation-reservation.md). It is not
called by `DesktopRemoteWindowHostCoordinator` and introduces no Platform,
Security, Transport, or `CreateProduction()` integration.

The state path is `Collecting -> Armed -> RouteAdmitted -> RouteSelected ->
PrepareSending -> ReadyMatched -> Promoted`, with one irreversible `Terminal`
alternative before promotion. Source, Permission, Authorization, Connection,
Emergency Stop, and Protection each receive a distinct opaque epoch. A bundle
can be claimed once; both host generation and exact epoch must match an
invalidation. Fact reasons come from a fixed allowlist, not injected callback or
exception text. Terminal completion is single and asynchronous-continuation
safe.

The nine deterministic tests use bounded `LongRunning` workers, barriers, and
completion sources without sleeps. They cover `M < R`, `R < M < S`, `S < M`,
route side-effect-then-throw, deadline equality at Arm, route, Prepare send,
Ready, and promotion, bundle reuse, stale host/fact ABA, simultaneous six-fact
invalidation, and exact Ready/promotion phase and binding. TDD REDs included the
missing core, missing deadline terminal, foreign Ready without fail-close,
bundle reuse, late canary reason throw/leak, and a missing Collecting phase.

Strict review initially returned BLOCK with one P1 and two P2 findings. After
the Collecting/Arm, single-claim bundle, complete deadline, and fixed-reason
repairs, final review returned APPROVE with 0 P0, 0 P1, and 0 P2 findings.
Focused Debug/Release passed `9/9`, Desktop Debug/Release passed `590/590`, and
solution Debug/Release passed `2304/2304`; both warning-as-error builds had zero
warnings/errors, and format, diff, vulnerability, explicit composition, and
simulator gates passed. Exact commands and limitations are in the
[core evidence](../evidence/2026-08-30-host-preparation-reservation-core.md).
Exact-SHA CI `33279540958` and CodeQL `33279540956` pass on evidence commit
`fa70e63`, which contains the core: downloaded artifacts prove `2304/2304` on
each hosted OS, Gitleaks 208/0, CodeQL 52/0, and reproducible unsigned packages.

These tests exercise only the isolated core and therefore change no matrix cell.
They do not prove a real source callback, permission revision, Trust mutation,
connection revocation, Emergency Stop registrar, or protection observation is
linearized with route or wire admission.

Subsequent exact commit `3d27389de16bcdc43722ac3a94220511f563edb1`
implements the atomic Platform source-invalidation slot, generation-bound
authenticated responder-route operation, and actual Transport Prepare
send-admission hook. Focused source and Transport seam tests pass `10/10` and
`32/32` in Debug and Release; both full solutions pass `2328/2328`. Exact-SHA CI
`33280551919` and CodeQL `33280551900` pass with the same `2328/2328` on every
hosted OS, Gitleaks 208/0, CodeQL 52/0, and reproducible unsigned packages. The
[admission-seam evidence](../evidence/2026-08-30-host-preparation-admission-seams.md)
records the exact jobs, artifacts, digests, and limits. That commit still does
not connect the Desktop reservation/coordinator to the seams, so it changes no
matrix cell. The subsequent source-only integration is recorded below; it does
not supply the remaining fact reservations or complete H0/H1 matrix.

### 2026-08-30 Host Preparation source linearization

Exact commit `ec63942296175f63964d8f463335d6b621e22042` implements the first
production-composed fact vertical. One Desktop reservation is registered in the
exact Platform source lease, passed through the authenticated responder-route
operation and actual Transport Prepare send-admission hook, matched with Ready,
and promoted only after post-Ready source/protection revalidation and formal
safety-owner installation. Cleanup uses the reservation's conservative route-
ownership state.

The focused host class injects source invalidation before route, after route
admission, during route failure, after Prepare send admission, during Prepare
failure, and concurrently with exact caller cancellation. It proves stable
`native_source_stale`, zero later authority, post-route fail-close, and exact
caller-token preservation. These rows use a coordinator connection double to
freeze the exact boundary.

The new production-composed tracer
`SourceInvalidationAfterReservedRoutePreventsPrepareWireAndDrains` freezes the
real `R < M < S` order. It establishes loopback TCP, authenticated protocol 1.7,
a production connection lease, and a real responder route; pauses before the
Prepare forward; unregisters the exact production source; and observes a
Source/`ConsumeConnection` terminal state. The later real Transport
send-admission attempt returns NotDelivered with zero Prepare wire, policy,
attachment wait, capture, media, renderer, render, or Admission. The source is
not reacquirable, fail-close and Dispose run once, and both nodes' routes,
directories, handlers, leases, controller, and coordinator state drain. The
existing DriverEligible success tracer traverses the same reservation, route,
send, Ready, promotion, media, input, and Emergency Stop path.

Focused host Debug/Release passes `44/44`, the new tracer `1/1`, Desktop
Debug/Release `596/596`, and solution Debug/Release `2334/2334`. Both warning-
as-error builds have zero warnings/errors; format, diff, vulnerability, explicit
composition, simulator, and final strict review pass. Exact-SHA CI
`33281547016` and CodeQL `33281546949` pass; downloaded artifacts prove
`2334/2334` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and reproducible
unsigned packages. Exact commands, job/artifact IDs, digests, and limitations
are in the
[source-linearization evidence](../evidence/2026-08-30-host-preparation-source-linearization.md).

This proves only Source `R < M < S` plus the success path. At that checkpoint,
production-composed Source `M < R` and `S < M`, Permission, Trust/Capability,
authenticated Connection mutation, Emergency Stop reserve/promote, Protection,
and every boundary's complete reject/throw/cancel/timeout/revoke/disconnect/
cleanup-fault matrix remained open.

### 2026-08-30 Host Emergency Stop readiness reservation

Exact commit `8e349cc7d9f722caa7e6df404ec6a59117d7d588` composes the
Emergency Stop fact through one managed process-local registrar slot and the
Desktop host reservation. Readiness reservation binds exact owner and Session
generations before route admission but installs no formal callback. Registrar
loss and promotion linearize under the registrar gate; promotion transfers the
same owner to the formal callback only after Ready, media attachment, host-fact
revalidation, and a fresh formal protection observation.

Platform tests cover no-callback reservation, conflict and release, stale ABA,
loss before and after promotion, promotion-versus-loss, registrar disposal,
invalidation-sink failure, retained repeat-disposal failure, and slot reuse.
Focused coordinator tests inject readiness loss before route, after route, and
after Prepare send; cancellation before/after promotion; promotion rejection,
throw, and side-effect-then-throw; immediate registration loss/disposal; and
formal-owner cleanup ordering. These rows freeze all three order shapes but do
not substitute for production-composed evidence.

The production-composed tracer
`EmergencyStopReadinessLossAfterReservedRoutePreventsPrepareWireAndDrains`
proves Emergency Stop `R < M < S` over real loopback TCP, authenticated
protocol 1.7, the production connection lease, responder route, managed
registrar, Desktop reservation, and actual Transport send-admission hook.
Readiness loss makes the reservation terminal with
`emergency_stop_readiness_unavailable` and `ConsumeConnection`; the send hook
runs once but admits no Prepare wire. Policy, attachment wait, capture, media,
renderer, render, and final Admission remain zero, and both nodes' owned graph
drains without resurrection.

Platform and Desktop Debug/Release pass `239/239` and `608/608`; both solution
configurations pass `2355/2355` with zero build warnings/errors, and format,
diff, vulnerability, explicit composition, and simulator gates pass. Exact-SHA
CI `33283264188` and CodeQL `33283264254` pass; downloaded artifacts prove
`2355/2355` on every hosted OS, Gitleaks 208/0, CodeQL 52/0, and reproducible
unsigned packages. Final strict review returned APPROVE with 0 P0/P1/P2 after
two initial P1 findings and one later P1 finding were repaired. Exact commands,
jobs, artifacts, digests, and limitations are in the
[Emergency Stop readiness evidence](../evidence/2026-08-30-host-emergency-stop-readiness-reservation.md).

This proves only the managed process-local registrar and one production-
composed order. At that checkpoint the other Emergency Stop `M/R/S` orders,
its complete fault matrix, native hotkey/action and physical behavior, Source
`M < R` and `S < M`, Permission, Trust/Capability, authenticated Connection
mutation, Protection, and the complete production-boundary matrix remained
open.

### 2026-08-30 Host Trust and Capability Preparation reservation

Exact commit `635dc23ec0c8f2812d527e16135b3d9c40885788` composes the
Authorization fact from the real authenticated handshake fingerprint through
the Security mutation gate, a narrow Desktop adapter, the host Preparation
reservation, authenticated responder route, actual Transport Prepare
send-admission hook, promotion, and cleanup. ViewOnly reserves all of
`mirror.view`; DriverEligible reserves all of `mirror.view` and
`mirror.drive`.

Transport tests prove that the generation-bound Remote Window connection lease
retains the authenticated peer fingerprint and that a replacement key for the
same Device ID cannot retarget an old lease. Security tests directly cover
exact fingerprint and all-of Capability admission; exact rejection statuses;
Applied same-grant invalidation; revoke/regrant, replacement, and late-dispose
ABA; mutation/reservation ordering on both sides of the gate; invalidation
before `Changed` and active-session Stop; rejected/throw/cancel behavior;
caller-token preservation; non-fatal sink/Stop failure identity and order;
fatal exhaustion without wrapping; and stable disposal invalidation.

Focused Desktop tests inject Authorization invalidation before route, after
route but before Prepare send, and after Prepare send. They also cover reserve
reject, blank authenticated fingerprint, non-fatal redaction, raw fatal
exhaustion, exact caller cancellation, and success/cancellation registration
release counts. The production-composed tracer
`AppliedSameMirrorGrantAfterReservedRoutePreventsPrepareWireAndDrains` proves
only Authorization `R < M < S` over real loopback TCP, authenticated protocol
1.7, a real responder route, and actual Transport send admission. The same-
grant Applied mutation invalidates the exact reservation while source and
connection remain current; the later send gate emits no Prepare wire or later
authority, and both managed owner graphs drain.

Local Transport and Security Debug/Release pass `719/719` and `144/144`;
focused host plus tracer Debug/Release pass `87/87`; Desktop Debug/Release pass
`616/616`; and both solution configurations pass `2377/2377` with zero build
warnings/errors. Format, diff, vulnerability, explicit TEST MODE composition,
simulator, and final strict reviews pass. Exact-SHA CI `33284857461` and CodeQL
`33284857449` pass; downloaded artifacts prove `2377/2377` on Windows, Linux,
and macOS, Gitleaks 208/0, CodeQL 52/0, and all three reproducible version-
0.1.195 unsigned packages. Exact commands, jobs, artifact/package digests, and
limitations are in the
[Trust/Capability Preparation evidence](../evidence/2026-08-30-host-trust-capability-preparation-reservation.md).

This is managed contract, test-mode composition, and unsigned-package evidence.
The other production-composed Authorization orders and full fault
intersections remain open, as do Permission, authenticated Connection mutation,
Protection, remaining Source and Emergency Stop orders, and the complete
production-boundary matrix. H0/H1 stay P or M; Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, all native/physical/signing/notarization/release gates,
and the Goal remain open.

### 2026-08-30 Host Permission Preparation reservation

Exact commit `d607ed1c3217c9c4102c4b893d20da9a6845f02d` composes the
Permission fact from an exact prompt-free snapshot through its authoritative
accepted-observation gate, the Desktop host reservation, authenticated
responder route, actual Transport Prepare send-admission hook, promotion, and
cleanup. The reservation binds owner generation, revision, capture and input
facts, and the frozen role. ViewOnly requires Granted capture; DriverEligible
also requires Granted input.

The macOS permission-boundary tests prove exact snapshot/role admission,
owner-claim rollback, invalidation before ordinary observers, same-fact
stability, Revoked/Granted ABA resistance, both reservation/commit gate orders,
ordered sink failures, fatal failure identity, stable disposal failure, and the
existing preflight/request sequencing and observer-isolation behavior. These
tests use controlled interop for state transitions. The matching-host production
test reaches the prompt-free CoreGraphics preflight call only; it does not
perform or observe a real TCC revoke, and macOS input remains `Unsupported`.

The coordinator reserves the exact fact before early observers and route,
rechecks the same registration before host-reservation promotion, releases it
after promotion, and owns it through terminal cleanup. Focused tests freeze
`M < R`, `R < M < S`, and `S < M`, plus snapshot/role rejection, missing
reservation support, unexpected/fatal/currentness failure, exact and foreign
cancellation, owner-claim-then-throw, and release ownership. Existing live
permission observers remain defense in depth after promotion.

The 26th production-composed tracer,
`PermissionRevisionAfterReservedRoutePreventsPrepareWireAndDrains`, proves only
Permission `R < M < S`. After real authenticated protocol-1.7 route selection,
a managed Granted-to-Revoked commit invalidates the exact reservation. The
actual Transport send gate admits no Prepare wire; regrant cannot revive the
terminal generation; participant policy, attachment wait, capture, media,
renderer, Admission, and input remain closed; and both managed owner graphs
drain. The tracer does not instantiate the macOS boundary and therefore is not
native permission evidence.

Local Platform, macOS Platform, Desktop, and complete solution Debug/Release
runs pass `240/240`, `64/64`, `639/639`, and `2418/2418` respectively. Both
warning-as-error builds report zero warnings/errors; format, diff,
direct/transitive vulnerability, explicit TEST MODE composition, and simulator
gates pass. Final independent review reports no P0/P1 finding for this scope.
Exact-SHA CI `33286525528` and CodeQL `33286525529` pass; retained artifacts
prove `2418/2418` on every hosted OS, Gitleaks 208/0, CodeQL 52/0 with no open
alerts, and three verified version-0.1.196 reproducible unsigned packages.
Exact commands, jobs, artifact/SARIF/package digests, and limits are in the
[Permission Preparation evidence](../evidence/2026-08-30-host-permission-preparation-reservation.md).

This checkpoint proves no real macOS TCC grant/deny/revoke/recovery,
Accessibility or input, Windows or Linux native permission boundary, physical
two-Device path, signed package, notarization, or release acceptance.
Production-composed Permission `M < R` and `S < M`, its remaining fault
intersections, authenticated Connection mutation, Protection, the remaining
Source/Authorization/Emergency Stop orders, and the complete matrix remain
open. H0/H1 stay P or M; Tasks 5, 5.5a, and 5.5, `CreateProduction()`, every
native/physical/release gate, and the Goal remain open.

### 2026-08-30 Host authenticated Connection Preparation reservation

Exact commit `259c3bbda4648bc6c45b71d78fbc7a34feb4de71` composes the
Connection fact across both authorities that determine an authenticated Remote
Window connection's currentness: the exact
`RemoteWindowConnectionGeneration` and its exact
`AuthenticatedRemoteWindowMediaSession`. One synchronous registration occupies
both slots, transfers cleanup ownership before returning, and is current only
while it remains the exact active object in both slots. Failed ownership
transfer rolls both slots back; monotonic registration IDs and exact-object
release prevent late-old-dispose ABA.

Transport tests directly cover generation revoke, explicit/deferred fail-close,
media Dispose, first control stop, and responder-route invalidation; each makes
the Preparation registration terminal under its authoritative gate before
ordinary generation or media control-stop callbacks. They also cover conflict,
owner-claim rollback, registration replacement, shared fail-close, ordered
non-fatal sink/cleanup failures, cleanup-before-raw-OOM, and the composite live
callback's partial-setup rollback, concurrent exact-once invocation, self-
dispose/fail-close re-entry, reverse-order release, stable failure replay, and
fatal cleanup.

Responder-route selection and actual Prepare send admission carry the exact
registration through fixed generation-to-media lock order. Public, foreign,
stale, missing, or other-lease owners cannot bypass an active reservation.
`ActiveConnectionPreparationBlocksPublicPrepareSendAtWireAdmission` is the
direct actual-wire-gate evidence: two leases share one real
`RemoteWindowControlSession`; one owns the registration, the other reaches the
public Prepare wire boundary, and zero Prepare frames are written.

The coordinator reserves Connection after source and Permission but before
ordinary safety observers and route, rechecks it before host-reservation
promotion, and releases it after promotion or terminal cleanup. Focused tests
freeze `M < R`, `R < M < S`, and `S < M`; reserve conflict, unexpected and
currentness throws, foreign cancellation, exact caller cancellation,
owner-claim-then-throw, raw OOM, release, and cleanup have independent rows.
Unexpected/foreign non-fatal failures expose only
`authenticated_connection_stale`; exact caller cancellation retains its token.

Two production-composed managed tests bring the tracer class to 28 executions.
`AuthenticatedControlDisconnectAfterReservedRoutePreventsPrepareWireAndDrains`
disconnects real authenticated protocol-1.7 control after a real responder
route is owned. It proves terminal Connection classification, no later
authority, and complete two-node drain. The disconnect cancels execution before
the actual send-admission callback, so `PrepareSendAdmissionCount == 0` in this
tracer is not send-gate rejection evidence; the Transport two-lease test above
is that evidence.

`MediaMutationAfterPreparationPromotionTriggersLiveCallbackBeforeCapture`
reaches Ready and verified bilateral `FSM1` attachment, then commits media
control stop during the exact promotion-to-temporary-registration-release
window. The live registration, which observes both generation and media paths
through one exact-once callback, crosses Emergency Stop before the mutation
returns and before capture starts. Admission, frames, input, and rendering stay
closed and every managed owner drains.

Transport and Desktop Debug/Release pass `755/755` and `654/654`; the focused
media-session class passes `41/41`; both full solution configurations pass
`2469/2469`; both warning-as-error builds have zero warnings/errors; and format,
diff, vulnerability, explicit TEST MODE composition, simulator, and two final
reviews with zero P0/P1 finding pass. Exact-SHA CI run `33289550263` has
successful Windows, macOS, Linux, and Secret Scan jobs: each platform's 12 TRX
files aggregate to `2469/2469` with all non-success counters zero, and Gitleaks
reports 208 rules with 0 results. CodeQL run `33289550265` passes 52 rules with
0 results and 0 exact-ref open alerts. All three reproducible version-0.1.197
unsigned packages pass their `SHA256SUMS`, repository verifier, exact
commit/runtime/unsigned metadata, archive, manifest, and canonical-tree checks.
Exact commands, artifacts, digests, and limitations are in the
[Connection Preparation evidence](../evidence/2026-08-30-host-connection-preparation-reservation.md).

This remains managed same-host loopback and contract evidence on macOS, not
native or physical Windows/macOS/Linux proof. It does not complete every
Connection reject/throw/cancel/timeout/revoke/disconnect/cleanup-fault
intersection. At that exact checkpoint, Protection still lacked an exact
Preparation reservation. Every aggregate H0/H1 status remained conservative,
and Tasks 5, 5.5a, and 5.5, `CreateProduction()`, every native/physical/signing/
notarization/release gate, and the Goal remained open.

### 2026-08-30 Host native Protection Preparation reservation

Exact commit `c987ca84e1f9f867f0edef3222a94dc8d25a2583` binds one
Protection Preparation registration to the complete accepted observation:
owner, session, and source generations; revision; kind; observation time; and
source identifier. Reservation requires the same current observation and a
fresh `Safe` snapshot, transfers owner cleanup synchronously under the source
mutation gate, rolls back a failed claim, and uses monotonic registration IDs so
a late old Dispose cannot remove a replacement.

Exact evidence/test-stabilization commit
`457a2c4b9e3d6905218e826cedd60029bbd1b35e` preserves that production
implementation and makes the formal source-loss test join deterministic
asynchronous terminal cleanup before inspecting the coordinator snapshot.

The host additionally binds the observation interval from
`ObservedAt - MaximumFutureClockSkew` through
`ObservedAt + MaximumProtectionAge`. The interval endpoints are valid, while
request-deadline equality is expired and takes priority. Arm, route admission,
actual Prepare send admission, Ready matching, and host promotion all recheck
the interval. The protection source separately revalidates exact identity and
freshness at formal promotion and at a fresh post-`Starting` capture-start
admission immediately before source use/native capture.

The same owner moves through `Temporary → FormalPreStart → Live`. Temporary
mutation invalidates the host reservation under the source gate before ordinary
observers. Formal-pre-start mutation synchronously closes controller Protection
admission and prevents capture. Only a current fresh-Safe capture-start gate
marks the registration Live. Live observations latch under the source gate,
close admission immediately, and notify outside that gate before ordinary
observers; source loss is terminal.

The host formal sink retains a bounded FIFO of exact observation/admission-epoch
pairs. Non-reentrant Notify calls wait until their observed sequence drains.
Active notification ancestry avoids waiting on its own callback stack while the
active outer drainer remains responsible for queued work. Reversed concurrent
notifications retain unsafe-before-Safe order, stale captured contexts join the
current drainer, source loss cannot be overwritten, and overflow/failure fail
closed. The underlying source also commits latest state before callbacks,
coalesces its bounded notification overflow to `Unknown`, joins in-flight formal
work during external Dispose, and avoids self/cross-source disposal deadlocks.

The controller's `ProtectionAdmissionUse` makes live protection admission a
real use boundary rather than another point check. Every native frame
destination and native or semantic input call holds one exact use throughout
the local boundary. Unsafe latch closes new use admission first. A current Safe
reconciliation cannot reopen until prior uses drain and its exact admission
epoch, observation, Active lifecycle, and Capturing state still match. Active
callback ancestry avoids waiting on itself without authorizing a new use.

Platform contract/controller tests cover exact identity and freshness,
conflict, claim rollback, ABA, all three ownership phases, source loss,
promotion/capture admission, reversed Notify/drain/ancestry, overflow, stable
failure identity, frame/input use scopes, safe reopen, cancellation, and Stop
races. Focused Desktop tests cover abstract `M < R`, `R < M < S`, and
`S < M`; both sides of capture-start admission; formal FIFO/source-loss drain;
reserve/promote/currentness failures; exact cancellation; raw fatal exhaustion;
and cleanup ownership.

The managed success case and the `SecureInput`/`Unknown` executions of
`ProtectionMutationAfterReservedRoutePreventsPrepareWireAndDrains` pass `3/3`
in Debug and Release. The negative rows use real authenticated protocol-1.7
loopback, select the production route, then commit Protection `R < M < S`. The
actual Transport send-admission hook is entered and returns `NotDelivered`, so
no Prepare wire or later capture/render/input/Admission authority opens; later
Safe cannot revive the terminal generation and both nodes drain. This is not
evidence for production-composed `M < R` or `S < M`.

On implementation tree `c987ca8`, Platform and Desktop Debug/Release pass
`289/289` and `700/700`; both solution configurations pass `2564/2564`; both
warning-as-error builds have zero warnings and errors; and format, diff,
direct/transitive vulnerability, explicit TEST MODE composition, simulator, and
two independent zero-P0/P1 final reviews pass. On test-only tree `457a2c4`, the
focused formal-source-loss Release row, 50 repeated local executions, and both
full `2564/2564` solution configurations pass. Exact-SHA CI `33294103546` and
CodeQL `33294103609` pass; downloaded artifacts prove `2564/2564` on Windows,
macOS, and Linux, Gitleaks 208/0, CodeQL 52/0 with 0 exact-ref open alerts, and
all three reproducible version-0.1.200 unsigned packages. Exact commands, jobs,
artifacts, digests, and limitations are in the
[Protection Preparation evidence](../evidence/2026-08-30-host-protection-preparation-reservation.md).

This checkpoint proves managed contracts and same-host loopback only. It does
not prove native Windows/macOS/Linux protection APIs, native capture/input,
physical Devices, packaged accessibility, signing, notarization, or release
acceptance. The complete per-boundary reject/throw/cancel/timeout/revoke/
disconnect/cleanup-fault matrix remains open, all H0/H1 aggregates stay P or M,
and Tasks 5, 5.5a, and 5.5, `CreateProduction()`, every native/physical/release
gate, and the Goal remain open.

### 2026-08-30 pending renderer authenticated disconnect

Exact commit `8d0831d0716bc68bc1d5dc0ff18c4efc033624b7` adds the 31st
production-composed managed tracer execution across TX, P0, P2, and CL. It uses
authenticated protocol-1.7 loopback and waits until Prepare send is admitted,
both exact `FSM1` media sessions are attached, and the participant renderer
factory is inside a deliberately non-cooperative Preparation call. No Ready
outcome exists at that point.

Authenticated control disconnect enters participant-owned cleanup and cancels
the Preparation lifetime. The renderer observes cancellation but does not
return, so disconnect, the worker, and renderer preparation correctly remain
incomplete before explicit release. Meanwhile the host's exact reservation is
terminal as Connection / `authenticated_connection_stale` with connection-
consuming cleanup. This is evidence that cleanup starts and cancels without
fabricating completion of a non-cooperative owner.

After release, the participant produces one local terminal
`Rejected/preparation_cancelled` result and disposes the late renderer. The host
never acknowledges Ready and opens no final Admission, capture, media send,
render, or input authority. Both-node controller, capture/input/session,
protection, permission observer, Emergency Stop, renderer, media, route,
directory, handler, channel, connection, and control owners drain.

The deliberately inverted focused RED sentinel failed `0/1`. Restored focused
Debug/Release each pass `1/1`; twenty fresh processes per configuration pass
`20/20`; the tracer class passes `31/31`; Desktop passes `701/701`; and the
solution passes `2565/2565`, all in both Debug and Release. Both warning-as-
error builds report zero warnings/errors, and format plus diff verification
pass. Exact-SHA CI `33295825931` and CodeQL `33295825897` pass; downloaded test
artifacts prove `2565/2565` with every non-success counter zero on each hosted
OS, Gitleaks reports 208/0, CodeQL reports 52/0 with 0 exact-ref open alerts,
and all three reproducible version-0.1.202 unsigned packages pass `5/5`
checksums and repository verification. Exact jobs, artifacts, digests, commands,
and limitations are in the
[pending-renderer disconnect evidence](../evidence/2026-08-30-pending-renderer-authenticated-disconnect.md).

This single row changes only P0 Disconnect from M to P. TX, P2, and CL remain
partial, every other cell is unchanged, and the remaining transaction phases,
Trust/lease revocation, renderer timeout, cleanup-fault combinations, and non-
cooperative native teardown remain open. This is same-host managed macOS
loopback evidence, not native or physical Windows/macOS/Linux proof. Tasks 5,
5.5a, and 5.5, `CreateProduction()`, every native/physical/signing/notarization/
release gate, and the Goal remain open.

### 2026-08-30 pending renderer exact deadline

Timeout implementation commit `40d4f78f32bb9958c1e7fbc075b6743620d1f0de`
adds the 32nd production-composed managed tracer execution across TX, P0, P2,
and CL. Final CI-stabilized evidence tree
`de4009aae9b7e5822983e13e70909b7deb8c2b64` preserves that timeout
behavior and hardens two independently exposed shutdown races.

The timeout row uses separate manual host and participant clocks after exact
Prepare send admission and bilateral verified `FSM1` attachment. Only the
participant advances to exact request-deadline equality while renderer
Preparation is non-cooperatively blocked. Host time remains before the deadline,
peer disconnect has not entered, and renderer cancellation is observed before
release. Release produces one `Rejected/preparation_expired`, then disconnect;
the host accepts only the bounded causally related terminal tuples documented in
the exact evidence. No Ready authority, Admission, capture, media send, render,
or input opens, and the late renderer plus both-node owner graph drain.

This fault originates at P2 timeout. It changes only P2 Timeout from M to P. CL
Timeout remains M because cleanup does not time out and no cleanup-timeout policy
is injected; every other matrix cell is unchanged.

Final local verification passes focused deadline/disconnect `2/2`, fresh
deadline Debug and Release `10/10` each, tracer `32/32`, Desktop `707/707`, and
solution `2571/2571` in both configurations. Both warning-as-error builds report
zero warnings/errors, and format plus diff checks pass. Three strict review
rounds report zero P0/P1/P2 after the exact classifier and dedicated publication-
worker repairs.

CI `33296383742` for earlier tree `c761acf` is failure evidence only: Windows
job `99216650548` exposed the exact stale-aggregate classification gap and local-
pairing `Task.Run` publication starvation. CodeQL `33296383740` succeeded but
does not make that CI run successful. Implementation-tree CI `33297152942` and
CodeQL `33297152906` pass for `40d4f78`. Final exact-SHA CI `33298564630`
and CodeQL `33298564676` pass for `de4009a`; downloaded artifacts prove
`2571/2571` with every non-success counter zero on each hosted OS, Gitleaks
208/0, CodeQL 52/0 with 0 exact-ref open alerts, and all three reproducible
version-0.1.205 unsigned packages pass `5/5` checksums and repository
verification. Exact jobs, artifacts, digests, commands, run history, and
limitations are in the
[pending-renderer deadline evidence](../evidence/2026-08-30-pending-renderer-deadline.md).

This remains managed same-host evidence, not native/physical Windows/macOS/Linux,
signing, notarization, or release proof. Tasks 5, 5.5a, and 5.5,
`CreateProduction()`, every native/physical/release gate, and the Goal remain
open.

Chunker and assembler tests cover every 64-KiB boundary through 16 chunks and the
1-MiB logical-frame ceiling, continuous sequence overflow, wrong binding/kind/
count/index/order, empty chunks, aggregate overflow, allocation/add/copy faults,
partial interruption/rejection, idempotent disposal, and zeroing of every transferred or
rejected owner. The capacity-one logical sender keeps only the latest pending
frame, never has more than one wire chunk outstanding, maps peer/session
backpressure to a bounded drop, and settles active/pending payloads under stop,
replacement, sink failure, cancellation, throwing or blocking cancellation
callbacks, and concurrent disposal. Queue teardown attempts every cleanup stage
and releases its budget even when cancellation or sink disposal throws.

Desktop codec tests freeze the finite JPEG ladder (original dimensions at quality
82/68/54, then 3/4 and 1/2 at 68/54), exercise every possible first-fitting
candidate, bounded failure-to-encode, alpha discard, legal padded BGRA8888 stride
behavior, pooled scratch clearing, and idempotent encoded/decoded owner disposal
that zeros already-borrowed managed memory. Code review additionally requires the
source/scaled Skia pixel spans and native encoded copy to clear before release. A
fixed 397-byte JPEG with SHA-256
`f294e425eda6aea42373311b447ac5518eabe2a897304b5a90c9a25ae3c8095e`
proves decoder compatibility without freezing OS-specific encoder bytes. Hostile
tests reject empty or over-1-MiB payloads before codec use, non-JPEG and animated
content, truncation, concatenated/trailing images, non-TopLeft orientation, and
invalid dimensions plus the exact combined 16,777,216-pixel/64-MiB BGRA boundary
before pixel allocation. There is no separate unreachable decoded-byte status.
The marker walker additionally rejects zero/one-length segments, segment overrun, and
scan-only markers outside entropy data while accepting legal fill bytes,
progressive multi-scan images with stuffed entropy bytes, and declared restart
markers.

Task 4 now has local and exact-commit hosted portable contract evidence in
`docs/evidence/2026-08-27-native-remote-window-media-contracts.md`. The hosted
Windows, macOS, and Linux results prove the named contract, headless, and unsigned
package paths only. They do not prove that the production listener classifies
`FSM1`, that the Desktop runtime owns and renews a media route, that native capture
or rendering works, or that two physical Devices meet interactive quality,
firewall, permission, or protected-surface requirements. The media
`SecureFrameSession` has no live rekey:
before either direction would exceed `2^20` protected frames, 1 GiB of plaintext,
or a sequence/epoch boundary, the runtime must terminate the attachment and its
owning authenticated control connection, complete a fresh authenticated control
handshake, and derive a new media session and route. Tests must never raise those
budgets or reuse the consumed route as a recovery path.

Task 5 Transport candidate evidence is recorded separately in
`docs/evidence/2026-08-28-native-remote-window-transport-candidate.md`.
Transport composition commit `f430705` and Task 5.4 implementation commit
`a75afb142c335d8da71e511c29e51b14ad2b3cf7` have exact-tree local macOS evidence.
On the latter tree, Transport passes 460/460 in Debug and Release, Security passes
131/131 in Debug and Release, the full Release solution passes 1878/1878, and the
warning-as-error build reports zero warnings. Exact-commit CI `33109385771`
passes 1878/1878 tests on Windows, macOS, and Linux plus Secret Scan and all three
reproducible unsigned package jobs; CodeQL `33109385769` also passes. These are
managed contract and loopback results, not native or physical evidence. A single
end-to-end case combining budget exhaustion with an injected cleanup failure
remains a P2 residual; the suite currently proves budget recovery and cleanup-
failure preservation separately. Desktop capture/encode/decode/render
composition, native adapters, packaged accessibility, physical Devices, and
interactive-quality requirements remain open regardless of these managed results.

Headless Desktop tests additionally block and reorder permission, service, and
observer callbacks. A permission busy-state observer may synchronously request
disposal without waiting on its own callback lease; external callers still join
the complete shared cleanup. Concurrent and later Desktop runtime disposers also
join one completion task and observe the same success or cleanup failure.
Authoritative inactivity or caller cancellation
before a late successful Start result forces local Emergency Stop, while an old
result cannot stop a newer same-controller replacement session before or after
that replacement ends. A lower-revision DriverEligible snapshot is rejected
before safety-role elevation and cannot trigger an input-permission stop against
the accepted current view-only session. These are portable local-gate and Desktop
contracts, not media, authenticated protocol, native capture/input/protection,
physical Device, operating-system permission, or real accessibility evidence.

Core invariants are asserted after every event:

1. a move never removes the only acknowledged instance, and closes the source
   only after a verified target receipt;
2. aborted/uncommitted swap leaves original placements;
3. terminal outcomes do not change;
4. operation ID/digest is one-to-one;
5. at most one live driver lease epoch authorizes input;
6. unauthorized peers never observe descriptor content;
7. a receipt cannot acknowledge another pending Activity or repeat descriptor
   payload;
8. diagnostics contain no registered canary secret;
9. replace never resumes incoming work unless an exact target snapshot has a
   verified, stored, unexpired undo capsule; undo applies only to the exact
   replacement and consumes the capsule once;
10. Replace target inventory never discloses payload/origin or ineligible target
    metadata, never exceeds one 64-item canonical page, and never authorizes
    mutation without destructive ID/revision/digest revalidation.
11. revoked Swap authority can converge only through an exact durable
    Operation/correlation/peer binding, and a silent peer cannot retain a pending
    correlation beyond its defined deadline.
12. a saved Scene has one explicit bounded Activity order and no representable
    descriptor payload, session key, reservation, capability snapshot, or Undo
    Capsule field; mutable Group membership cannot silently expand it at apply.
13. a Scene source is never inferred: multiple exact-ID active placements
    require an exact user selection and full repreview, while a selected source
    already at the destination produces no operation or Adapter call.
14. only a Scene exact-slot Empty result can authorize Require Empty and only
    one exact Eligible Conflict with Preserve Source can authorize confirmed
    Replace; occupied Move-plus-Replace, Opaque, Ambiguous, or filtered inventory
    absence always fails closed.
15. a Remote Window input reaches its local boundary only for one current
    participant, immutable Capability snapshot, fresh Safe protection state,
    exact live Driver epoch, and one exact `ProtectionAdmissionUse` held for the
    entire local call; native frame delivery holds the same kind of use through
    its destination. Emergency/protection preemption closes new uses first and
    any retired epoch cannot be reported as injected. Only the latest monotonic
    protection observation may publish Active or confirmed Paused state; re-
    entrant churn is bounded, partial resume failure re-closes both gates, and a
    stale resume cannot reopen admission before older uses drain or after
    Emergency Stop. Revocation cannot restore a peer whose local disconnect
    remains pending, and stop/reset truth is computed from cumulative local-
    boundary confirmation for only the current session generation.
16. a Remote Window Preparation grants no authority: only the source host reads
    its peer-relative grant; Ready cannot create a known participant binding;
    capture and frames remain closed until current host revalidation, Start,
    AddParticipant, and exact final Admission state all succeed; any terminal
    failure consumes the selected media session and requires a fresh
    authenticated connection.

## 4. CI matrix

Pull requests and protected branches run:

| Job | OS | Scope |
| --- | --- | --- |
| `test-linux` | `ubuntu-latest` | restore, format, Release build, all portable tests |
| `test-windows` | `windows-latest` | Release build, portable and Windows contract/native-safe tests |
| `test-macos` | `macos-latest` | Release build, portable and macOS contract/native-safe tests |
| `security` | Ubuntu | dependency audit, secret scan, CodeQL/static analysis where supported |
| `packages` | all three | produce installable unsigned test artifacts and smoke launch |

The SDK is installed from `global.json`; dependencies are locked and caches key
on lock files. Tests use invariant culture/time zone unless testing localization.
Native permission tests that hosted runners cannot grant are skipped only with a
stable reason code and appear in the evidence summary—not as passes.

The three `test-*` jobs also execute the desktop composition validator after the
headless UI tests. Native window launch and accessibility remain manual/matching-
machine gates even when all three validators pass.

## 5. Protocol compatibility policy

- Committed golden fixtures are decoded by the current reader.
- Current writers round-trip through the current reader canonically.
- The oldest supported minor fixture is included in every run.
- Protocol 1.0 is the oldest non-Swap control version; Swap first appears in the
  frozen 1.1 fixture and is not exposed on a negotiated 1.0 session.
- Protocol 1.5 freezes Remote Window control and encrypted media-frame fixtures;
  protocol 1.6 adds only the frozen authenticated `FSM1` media attachment; and
  protocol 1.7 adds the independently gated Prepare/Ready transaction. Every
  lower minor rejects the newer feature without semantic fallback.
- Required-field removal or semantic reuse requires a major version.
- Optional additions need old-reader behavior tests.
- Fuzz and hostile fixtures have bounded execution time and allocations.

## 6. Quality thresholds

Coverage percentages are diagnostic, not the acceptance target. All safety
invariants and error branches for pairing, authorization, move, swap, lease,
frame decoding, redaction, and emergency stop require explicit tests. New code
may not reduce branch coverage for those namespaces. Flaky tests are treated as
failures; quarantining requires an owner, issue, and expiry.

## 7. Manual evidence before v1

On at least one supported version of each OS:

- install, upgrade, uninstall, and launch at login;
- discover and pair two physical devices on a LAN;
- permission grant, denial, revocation, and subsequent recovery;
- semantic handoff plus visibly labelled Remote Window fallback;
- capture/input protection on sensitive surfaces;
- emergency stop under network loss and UI stress;
- sleep/wake, Wi-Fi change, peer restart, and version mismatch;
- screen reader, keyboard-only operation, scaling, reduced motion;
- artifact signing/notarization verification.

These results are recorded under `artifacts/evidence/<version>/` or linked from
the release record. The repository must not claim them before execution.
