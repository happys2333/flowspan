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

The release criterion still requires each tracer boundary to have reject, throw,
cancel, timeout, revoke, disconnect, and cleanup-fault cases. In particular, the
current nine scenarios are not the required matrix; its per-boundary
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

The hosted matrices are cross-platform managed contract evidence,
not evidence for native platform APIs, two physical devices, accessibility,
interactive quality, package signing, or macOS notarization.
`CreateProduction()` must keep Remote Window unavailable until the native and
authenticated runtime is composed into it. The tracer does not close Task 5,
Task 5.5a, Task 5.5, any native/physical/release gate, release criterion, or the
long-term Goal.

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
    participant, immutable Capability snapshot, fresh Safe protection state, and
    exact live Driver epoch; emergency/protection preemption and any retired
    epoch cannot be reported as injected. Only the latest monotonic protection
    observation may publish Active or confirmed Paused state; re-entrant churn
    is bounded, partial resume failure re-closes both gates, and a stale resume
    cannot reopen a gate after Emergency Stop. Revocation cannot restore a peer
    whose local disconnect remains pending, and stop/reset truth is computed from
    cumulative local-boundary confirmation for only the current session
    generation.
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
