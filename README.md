# Flowspan

Flowspan is a local-first continuity workspace for handing off, moving,
replacing, atomically swapping, mirroring, and grouping **Activities** across
Windows, macOS, and Linux.

An Activity is portable user intent plus context—not a screen and not an
arbitrary process image. Flowspan prefers semantic resume through an adapter. If
an application cannot provide it, the product may offer a clearly labelled
Remote Window that keeps execution on the source device.

## Project status

Flowspan is an **advanced portable and Desktop v1 candidate**, but it is not
release-ready or suitable for end-user installation. The repository currently
includes:

- approved v1 requirements, architecture, ADRs, domain glossary, threat model,
  test strategy, task tracker, and release checklist;
- a warning-free .NET 10 solution with an independent domain core;
- a deterministic two-node Semantic Handoff and journaled-coordinator Atomic Swap
  simulator, plus bounded protected per-Device Swap endpoint journals with
  exact restart reduction contracts;
- a protocol-1.1 authenticated Atomic Swap tracer that requests one exact
  Activity, persists Operation/correlation/peer-bound endpoint evidence, carries
  Prepare and the durable decision over an encrypted session, times out silent
  peers deterministically, and freezes all six JSON frames and SHA-256 hashes;
- capability denial, descriptor validation, idempotent retry, operation-ID
  conflict, protocol negotiation, and diagnostic-redaction tests;
- provisional, review-gated identity, pairing, trust, HKDF, and encrypted-frame
  primitives with negative tests;
- a bounded headless pairing ceremony with transcript proof, matching-SAS
  decision ports, signed completion proof, and direct-TCP loopback coverage;
- one bounded TCP listener that explicitly routes pairing or authenticated
  session hellos, then requires a new authenticated connection after pairing;
- bounded signed DNS-SD offers, an isolated provisional browser/publisher, and
  trusted-candidate/reconnect contracts;
- a bounded authenticated TCP listener that can route multiple currently
  trusted peers through one port and drain revoked sessions;
- a pinned Avalonia 12.1 desktop composition root that loads or creates the
  protected local device identity, exposes truthful non-sharing/empty states,
  and has headless keyboard and automation-metadata tests;
- a least-privilege incoming pairing-confirmation bridge for already verified
  security-core requests, with explicit code comparison, zero-capability
  default, cancellation/stale-prompt protection, and two-node loopback tests;
- an explicitly enabled desktop local-network lifetime that composes one
  listener, minimized DNS-SD browse/advertise, transcript-bound outgoing
  pairing, persistent Trust editing, and truthful per-peer trusted-reconnect
  status while remaining `NOT SHARING`; Activity and Mirror control grants are
  any-of connection admission only and every operation rechecks its exact
  purpose; starting that lifetime requires an acknowledged
  Windows/macOS/Linux-specific privacy preflight;
- one bounded `workspace.note/v1` desktop Semantic Handoff over the encrypted
  authenticated control channel, with directional Capability checks, an
  explicit source-preserving preview, a named unavailable Remote Window limit,
  and payload-free operation receipts;
- bounded `workspace.note/v1` desktop Semantic Move on that control channel,
  with a separate target-first preview, verified-receipt source cleanup,
  source-preserving failure/uncertainty, and an explicit duplicate warning when
  source cleanup fails;
- bounded `workspace.note/v1` desktop semantic Replace with purpose-scoped
  payload-free inventory, exact confirmation and send-time revalidation,
  current directional Trust checks, protected target-owned undo capsules,
  target-local recovery/undo, truthful acknowledgement-loss guidance, and
  keyboard/automation coverage;
- a portable Remote Window/Mirror control plane with bounded participant and
  input contracts, one current Capability snapshot per use, monotonic Driver
  Lease transfer/expiry/disconnect, protection pause/resume, and local
  emergency-stop preemption/fault results, plus strict protocol-1.5 authenticated
  control and purpose-separated bounded media framing, rate limits, backpressure,
  timeout, and cleanup tests;
- a local Desktop Remote Window candidate with an explicitly labelled
  source-hosted fallback, purpose-scoped mirror target selection independent of
  semantic receive targets, production Mirror-only control-channel admission,
  post-Trust-change refiltering, progressive permission review, persistent
  sharing and Driver/protection state, accessible Emergency Stop,
  generation-bound stale result rejection, and fail-closed teardown; production
  composition still uses an unsupported adapter until native
  capture/input/protection work is delivered;
- Windows/macOS/Linux CI definitions.

It does **not** yet provide physical-LAN discovery evidence, progressive native
permission integrations, native capture/input, a production Remote Window codec
or renderer, complete Activity desktop workflows, packaged native accessibility
evidence, signed/notarized real-machine install/upgrade/uninstall evidence, or the
complete Windows/macOS/Linux acceptance matrix. See
[the v1 task tracker](specs/v1/tasks.md) and
[release criteria](docs/release/v1-release-criteria.md) for the honest status.

## Run the current slice

Install the SDK version selected by [`global.json`](global.json), then run:

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Desktop/Flowspan.Desktop.csproj \
  --configuration Release --no-build --no-restore -- \
  --validate-composition
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
```

Desktop validation uses an explicitly degraded in-memory identity, prints TEST
MODE, and exits; it never substitutes for production platform storage. The
simulator uses fixed device, operation, and clock values. A successful run
prints protocol `1.5`, `Source preserved: True`, `Target resumed: True`,
`Atomic swap committed: True`, and a redacted operation receipt containing a
descriptor digest but no Activity text. Its Swap endpoints and catalog remain
process-memory tracers. Separate application and platform contracts implement
protected endpoint restart; the simulator does not yet compose that persistence
or substitute for physical restart evidence.

## Repository map

```text
specs/v1/                 requirements, design, and tracked implementation tasks
docs/adr/                 architectural decisions and trade-offs
docs/security/            threat model and security release blockers
docs/testing/             evidence model and test/CI matrix
docs/release/             criteria that gate any v1-complete claim
src/Flowspan.Domain/      platform-independent Activity and operation model
src/Flowspan.Application/ handoff use case, authorization, journal, adapter ports
src/Flowspan.Platform/    capability, protection-state, and input-safety contracts
src/Flowspan.Protocol/    protocol version negotiation primitives
src/Flowspan.Security/    provisional identity, pairing, trust, and AEAD primitives
src/Flowspan.Diagnostics/ redacted receipt serialization
src/Flowspan.Desktop/     Avalonia composition root and accessible local shell
src/Flowspan.Transport*/  direct secure transport, discovery, and reconnect boundaries
src/Flowspan.Simulator/   runnable deterministic two-node scenario
tests/                    domain, protocol, and integration tests
```

Start with [v1 requirements](specs/v1/requirements.md), then read the
[technical design](specs/v1/design.md) and
[ubiquitous language](UBIQUITOUS_LANGUAGE.md).

## Safety boundary

The current desktop composition can explicitly enable local pairing and an
authenticated control channel. It carries the bounded `workspace.note/v1`
Semantic Handoff, acknowledged Semantic Move, and protected semantic Replace.
The same control core can carry the protocol-1.1 Swap tracer under an independent
`activity.swap` grant, but the production Desktop intentionally exposes no Swap
command until exact confirmation and visible recovery are delivered. These are
one-shot descriptor operations rather than live sharing, so the global state
remains `NOT SHARING`. Move closes the source only after a verified target
receipt; Replace preserves the source and stores target undo state before target
mutation. Rejection, failure, or uncertainty preserves the source. Flowspan does
not transfer process memory, unsaved application internals, or credentials. The
portable protocol can authenticate bounded screen-media bytes and remote input,
and the Desktop has a headless presentation and progressive-permission candidate.
The production local-network control profiles admit `mirror.view` and
`mirror.drive` only to establish an authenticated idle channel; viewing still
requires `mirror.view`, and driving still requires both grants at each use
boundary. Production composition intentionally reports unsupported because it has no native
capture, production codec/rendering, protected-surface probe, or input-injection
adapter. The in-memory simulator and same-host loopback evidence do not substitute
for physical-device, native-permission, or independent security review gates.

Flowspan is a clean-room rewrite. See
[clean-room engineering and provenance](docs/engineering/clean-room.md).

## Contributing

The engineering baseline is intentionally strict: nullable analysis, .NET
analyzers, style checks, warnings as errors, locked packages, three-OS CI,
CodeQL, and secret scanning. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before
submitting changes.
