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
handlers with complementary one-way grants in either Device ID ordering; both
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

The task 7.3c tracer keeps Replace separate from Activity transfer. Application
tests require an exact target ID/revision/digest and prove capture or store
failure blocks before incoming resume, successful Replace stores a 15-minute
target-owned capsule, retries do not repeat capture/resume, and undo is
expiry-aware, exact-current, idempotent, and single-consume. Protocol tests use
strict `activity.replace` and `activity.replace.result` bodies, keep preserved
target payload out of results, reject target-snapshot tampering, fault closed on
forged capsule binding, classify lost acknowledgement as uncertain, and run one
real encrypted loopback Replace. The desktop endpoint and controls remain
deliberately uncomposed until remote target selection, destructive preview, and
visible local undo exist. In-memory and same-host evidence does not prove
restart durability, physical networking, native restoration, or a shipped UI.

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
8. diagnostics contain no registered canary secret.
9. replace never resumes incoming work unless an exact target snapshot has a
   verified, stored, unexpired undo capsule; undo applies only to the exact
   replacement and consumes the capsule once.

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
