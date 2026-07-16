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
not-yet-built Groups, Scenes, Remote Window, native Adapters, or physical fault
evidence.

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
