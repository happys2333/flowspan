# Evidence: macOS headless foundation, 2026-07-13

Classification: **Local** and **simulated/contract**

Branch: `codex/v1-foundation`

Source state: this document's repository revision

## Environment

```text
OS: macOS 26.5.2 (Darwin 25.5.0), arm64
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
```

## Commands and results

```sh
dotnet restore Flowspan.slnx --locked-mode
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release --no-build --no-restore
dotnet run --project src/Flowspan.Simulator/Flowspan.Simulator.csproj \
  --configuration Release --no-build --no-restore
dotnet list Flowspan.slnx package --vulnerable --include-transitive
```

Observed results:

- locked restore: passed for 22 projects;
- format verification: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 287 passed, 0 failed, 0 skipped;
  - domain: 33 passed;
  - protocol: 17 passed;
  - integration: 22 passed;
  - security: 82 passed;
  - platform contracts: 8 passed;
  - Linux identity/trust-store contracts/platform guards: 15 passed;
  - macOS identity/trust-store contracts/native smoke: 10 passed;
  - Windows identity/trust-store contracts/platform guards: 12 passed;
  - transport: 74 passed;
  - isolated mDNS/DNS-SD browser/publisher adapter: 14 passed;
- simulator: protocol 1.0 negotiated, source preserved, target resumed, process
  exit code 0;
- NuGet query: no known vulnerable package reported for any of the 22 projects.

The simulator receipt contained operation/correlation/device/Activity IDs,
`workspace.note/v1`, a full descriptor digest, timestamp, and `none` failure
code. It did not contain the Activity text.

## What this proves

- The headless solution restores and compiles on this one macOS arm64 host.
- The current domain validation, capability denial, protocol negotiation,
  bounded/canonical control codec, framed partial reads, semantic handoff,
  move ordering/recovery, deterministic delivery faults, sequential/concurrent
  idempotency, swap prepare/abort/commit recovery, mirror driver lease epochs,
  emergency stop, provisional identity/pairing/HKDF/AEAD primitives, bounded
  canonical pairing hello/signature/confirmation/completion messages, seeded
  hostile pairing decode containment, transcript proof before matching-SAS
  confirmation, completion-proof gating before Trust registration, deterministic
  reject/deadline/tamper/identity-conflict/persistence/cleanup behavior, complete
  two-store pairing over one real loopback TCP connection, strict initial
  `FSP1`/`FSH1` hello-family selection, same-port pair/close/authenticated
  reconnect, independent pairing/session/total capacity, serialized pairing
  registration/revocation, selection timeout and fatal-accept draining, trust
  identity-change/revocation behavior, fail-closed platform protection/input
  policy, signed discovery expiry/deduplication/identity-change behavior,
  canonical authenticated-handshake encoding, downgrade/key-substitution
  rejection, bounded handshake timeout, direct loopback TCP secure upgrade,
  authenticated control identity/version binding, bounded reconnect backoff,
  serialized reconnect supervision, transient/authenticated/permanent outcome
  policy, network-change cancellation and burst coalescing, no-overlap and
  permanent-rejection race safety, caller-cancellation draining, production
  wait cancellation, BCL network-change subscription lifecycle, cancellation
  callback fault containment, structured reconnect stop reasons, signed
  candidate/endpoint validation, current trust and capability reload, same-key
  device rename handling, transient/permanent handshake classification, real
  loopback authenticated-session composition, post-handshake registration race
  rejection, active peer-revoke/capability-downgrade handler draining, and
  authenticated disconnect classification, claimed inbound Device-ID routing
  with full trusted-key validation, two-current-peer authentication through one
  real loopback listener, bounded inbound handshake/session slots, capability
  denial, handler-failure isolation, current-key recheck before registration,
  and peer-specific/fatal-listener/caller-cancellation draining,
  bounded canonical DNS-SD TXT chunking/decoding, randomized hostile TXT
  containment, current-trust candidate construction, dual-stack candidate
  rotation, unsafe-address rejection, split SRV/TXT/A/AAAA resolution,
  package-record translation, bounded discovery caches, full mDNS stack restart
  on injected network changes, canonical minimized publication profiles,
  immediate signed-offer publication, fresh-nonce refresh, cancellation
  withdrawal, stack-replacement replay, failed-publish rollback,
  publish/withdraw and startup/cleanup failure preservation, and
  bind/factory/subscriber/old-stack-cleanup fault isolation,
  atomic identity provisioning/restart/deletion with an explicitly degraded
  test store, bounded/canonical identity payload round trips and hostile-shape
  rejection, shared platform-payload bridging, Windows atomic protected-file
  identity create and trust replace semantics through a fake protector,
  separate trust/identity paths and context selection, cancellation preserving
  the old trust snapshot, concurrent first-start convergence,
  corrupt/cancel cleanup, non-Windows DPAPI rejection, native macOS Keychain
  identity create/reload/delete/concurrent-add and trust
  create/restart/update/revoke behavior, Linux secret-tool fake
  boundary identity/trust Base64/stdin/atomic-lock/replacement/error contracts,
  per-invocation output-limit selection, and non-Linux rejection,
  trust-revocation/capability-downgrade session shutdown ordering and failure
  fan-out, canonical bounded persistent-trust encoding/golden fixture, restart,
  identity-change refusal, corrupt-open rejection, concurrent mutation,
  cancellation, failed-save rollback, durable-revoke-before-stop ordering,
  awaited local-shutdown session draining, conflict detection, and receipt
  redaction cases behave as asserted by the tests.
- The deterministic simulator can resume a portable note on a second in-memory
  node without removing it from the source.

## What this does not prove

- Windows or Linux behavior on a physical Flowspan test machine. Hosted CI
  evidence through commit `657c9dd` is recorded separately in
  `2026-07-13-hosted-ci.md`.
- Actual Windows CurrentUser DPAPI execution; this macOS run exercised the
  production adapter's explicit platform rejection and fake-protector contract
  only.
- Linux `secret-tool` process execution or a live desktop Secret Service; Linux
  conditional identity/trust process-limit and cancellation tests did not
  execute on macOS. Hosted Ubuntu process-contract evidence through `8b3e11b`
  is recorded separately.
- Native Windows or Linux `ITrustPayloadStore` execution. The macOS trust test
  used a unique disposable Keychain item; Windows hosted evidence is recorded
  separately, while Linux still lacks a live Secret Service round trip.
- Physical LAN discovery or network-interface churn, desktop pairing UI or a
  two-person SAS comparison, Linux Secret Service, untested Keychain/DPAPI
  profile states,
  live DNS-SD browse/publication, remotely reachable multi-device listener
  behavior, independent security review, native permissions, capture, input,
  protected surfaces, Remote Window, UI,
  accessibility, packaging, signing, or update behavior.
- Resistance to an independent security review, fuzzing, or real hostile peers.

The local security and loopback tests exercise the macOS .NET crypto/network
providers and the provisional authenticated handshake format. They do not
constitute an independent security review or physical-device interoperability
evidence.

Those remain unchecked in `docs/release/v1-release-criteria.md`.
