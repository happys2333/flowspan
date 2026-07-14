# Flowspan

Flowspan is a local-first continuity workspace for handing off, moving,
replacing, atomically swapping, mirroring, and grouping **Activities** across
Windows, macOS, and Linux.

An Activity is portable user intent plus context—not a screen and not an
arbitrary process image. Flowspan prefers semantic resume through an adapter. If
an application cannot provide it, the product may offer a clearly labelled
Remote Window that keeps execution on the source device.

## Project status

Flowspan is at the **foundation plus early desktop shell** stage and is not ready
for end-user installation. The repository currently includes:

- approved v1 requirements, architecture, ADRs, domain glossary, threat model,
  test strategy, task tracker, and release checklist;
- a warning-free .NET 10 solution with an independent domain core;
- a deterministic two-node semantic handoff simulator;
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
- Windows/macOS/Linux CI definitions.

It does **not** yet compose the production network listener/discovery into that
pairing surface or provide trusted-device editing, physical-LAN discovery
evidence, native capture/input, Remote Window media, complete desktop workflows,
packaged native accessibility evidence, packaging, or the complete real-machine
Windows/macOS/Linux acceptance matrix. See [the v1 task tracker](specs/v1/tasks.md) and
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
prints protocol `1.0`, `Source preserved: True`, `Target resumed: True`, and a
redacted operation receipt containing a descriptor digest but no Activity text.

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

The current in-memory simulator does not provide production security. Real peer
traffic must not be enabled until the pairing, authenticated key exchange,
end-to-end encrypted framing, platform secret stores, authorization negatives,
and security review gates in the threat model are implemented.

Flowspan is a clean-room rewrite. See
[clean-room engineering and provenance](docs/engineering/clean-room.md).

## Contributing

The engineering baseline is intentionally strict: nullable analysis, .NET
analyzers, style checks, warnings as errors, locked packages, three-OS CI,
CodeQL, and secret scanning. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before
submitting changes.
