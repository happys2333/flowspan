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

- locked restore: passed for 14 projects;
- format verification: passed;
- Release build: passed with 0 warnings and 0 errors;
- tests: 124 passed, 0 failed, 0 skipped;
  - domain: 33 passed;
  - protocol: 17 passed;
  - integration: 22 passed;
  - security: 28 passed;
  - platform contracts: 8 passed;
  - transport: 16 passed;
- simulator: protocol 1.0 negotiated, source preserved, target resumed, process
  exit code 0;
- NuGet query: no known vulnerable package reported for any of the 14 projects.

The simulator receipt contained operation/correlation/device/Activity IDs,
`workspace.note/v1`, a full descriptor digest, timestamp, and `none` failure
code. It did not contain the Activity text.

## What this proves

- The headless solution restores and compiles on this one macOS arm64 host.
- The current domain validation, capability denial, protocol negotiation,
  bounded/canonical control codec, framed partial reads, semantic handoff,
  move ordering/recovery, deterministic delivery faults, sequential/concurrent
  idempotency, swap prepare/abort/commit recovery, mirror driver lease epochs,
  emergency stop, provisional identity/pairing/HKDF/AEAD primitives, trust
  identity-change/revocation behavior, fail-closed platform protection/input
  policy, signed discovery expiry/deduplication/identity-change behavior,
  canonical authenticated-handshake encoding, downgrade/key-substitution
  rejection, bounded handshake timeout, direct loopback TCP secure upgrade,
  authenticated control identity/version binding, bounded reconnect backoff,
  atomic identity provisioning/restart/deletion with an explicitly degraded
  test store, conflict detection, and receipt redaction cases behave as asserted
  by the tests.
- The deterministic simulator can resume a portable note on a second in-memory
  node without removing it from the source.

## What this does not prove

- Windows or Linux compilation/runtime behavior on a physical Flowspan test
  machine. Hosted CI evidence through commit `e464194` is recorded separately
  in `2026-07-13-hosted-ci.md`.
- Physical LAN discovery, the interactive pairing wire/UI ceremony, credential
  stores, multi-peer listener operation, independent security review, native
  permissions, capture, input, protected surfaces, Remote Window, UI,
  accessibility, packaging, signing, or update behavior.
- Resistance to an independent security review, fuzzing, or real hostile peers.

The local security and loopback tests exercise the macOS .NET crypto/network
providers and the provisional authenticated handshake format. They do not
constitute an independent security review or physical-device interoperability
evidence.

Those remain unchecked in `docs/release/v1-release-criteria.md`.
