# Evidence: macOS headless pairing ceremony, 2026-07-13

Classification: **Local**, **loopback integration**, and **simulated/contract**

Branch: `codex/v1-foundation`

Verified source commit: `79dae126e25d7b0472340dbe9159c3a0b8326c15`

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

- locked restore passed for 22 projects;
- format verification passed;
- Release build passed with 0 warnings and 0 errors;
- 268 tests passed, 0 failed, and 0 skipped;
  - security: 71 passed, including 15 ceremony and 6 pairing-wire tests;
  - transport: 66 passed, including the direct-TCP pairing loopback test;
- the simulator negotiated protocol 1.0, preserved the source, resumed the
  target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

## What this proves

- Canonical pairing messages are limited to 4096 bytes. A committed hash freezes
  the v1 hello encoding, and 512 deterministic hostile inputs were submitted to
  each of the four decoders without escaping the bounded format-error contract.
- Both roles negotiate the highest common version and verify the peer's
  role-ordered transcript signature before invoking the local decision source.
- The two local decision sources receive the same six-digit SAS and independently
  choose only the Capabilities their own device grants the peer.
- Rejection cancels a pending peer prompt. Injected network and prompt deadlines
  return `Timeout`; neither negative path writes a Trust Record.
- Altered transcript signatures, signed confirmations, and distinct completion
  proofs are rejected. Completion proofs gate Trust registration so a tampered
  confirmation cannot make only the accepting endpoint write trust in the tested
  exchange.
- An identity-change result is emitted only after the claimed new key proves
  transcript possession. The old Trust Record is preserved. Re-pairing the same
  key reports `AlreadyTrusted` and does not silently replace its Capabilities.
- A local trust-save failure remains an error rather than local pairing success.
  Caller cancellation disposes the one-shot channel, and simultaneous protocol
  and cleanup failures remain present in one aggregate.
- One real loopback TCP connection carried the complete ceremony between two
  generated identities and two independent in-memory Trust stores.

## What this does not prove

- Decision sources were deterministic test doubles. No desktop confirmation UI,
  human SAS comparison, accessibility flow, or accidental-approval behavior was
  exercised.
- The TCP connection stayed inside one process and one host. It does not prove a
  physical LAN path, firewall behavior, or discovery-to-pairing composition.
  Same-port listener multiplexing was implemented and classified separately in
  [the unified-listener evidence](2026-07-13-macos-unified-listener.md).
- No Windows or Linux network provider ran locally. Matching hosted CI for
  commit `79dae12` is recorded in
  [the hosted CI evidence](2026-07-13-hosted-ci.md); those jobs are still
  loopback/contract evidence rather than physical-device networking.
- The provisional cryptographic protocol has no independent security approval.
  Completion proofs reduce asymmetric acceptance on tamper but do not claim
  impossible atomic commitment across arbitrary permanent network partitions;
  recovery and user-visible asymmetric-state handling remain release work.
- No live platform credential store was used by this ceremony test. Existing
  platform-store evidence is classified separately.

Desktop and physical-device acceptance remains open in the v1 task tracker and
release criteria.
