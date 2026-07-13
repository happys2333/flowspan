# Evidence: macOS DNS-SD browser contracts, 2026-07-13

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

- locked restore passed for 22 projects;
- format verification passed;
- Release build passed with 0 warnings and 0 errors;
- 226 tests passed, 0 failed, 0 skipped;
  - `Flowspan.Transport.Tests`: 51, including 10 DNS-SD TXT/candidate tests;
- `Flowspan.Transport.Mdns.Tests`: 9 adapter/cache/fault tests;
- all other suites retained their prior 166 passing tests;
- simulator reported protocol 1.0, source preserved, and target resumed;
- NuGet reported no known vulnerable direct or transitive package for any of
  the 22 projects.

The direct mDNS dependency is `Makaretu.Dns.Multicast` 0.27.0. Its exact locked
transitive graph and maintenance/license caveats are recorded in
`docs/engineering/dependencies.md` and ADR 0004.

## What this proves

- A maximum permitted device display name fits the 768-byte canonical offer
  payload, at most five Base64 TXT chunks, and the 255-byte DNS string limit.
- The reader rejects missing, malformed, non-canonical, oversized, duplicate,
  and deterministic random hostile TXT payloads without exposing a candidate.
- A candidate is produced only from a current trust record, a matching signed
  port/key/fingerprint, and concrete non-self addresses. Same-key display-name
  changes remain valid; untrusted peers, self offers, loopback, multicast,
  unspecified, and unscoped IPv6 link-local addresses do not become candidates.
- Multiple concrete addresses rotate across reconnect attempts; invalid refresh,
  service removal, offer expiry, start failure, and disposal preserve the
  expected candidate-source lifecycle.
- The isolated adapter combines split SRV, TXT, A, and AAAA observations; bounds
  record batches, instances, TXT properties, and addresses; translates actual
  Makaretu record types; and deterministically handles injected bind, restart,
  factory, subscriber, and diagnostic faults.
- An outer BCL network-change notification replaces the entire injected mDNS
  stack, withdraws the old cache, and issues a new browse query. If replacement
  construction fails, the old stack is retained and the failure is surfaced.

## What this does not prove

- The production adapter opened multicast sockets, emitted a query, received a
  packet, or discovered a second process/device on this host.
- IPv4/IPv6 interoperability, same-interface address churn, multiple interfaces,
  VPN routing, firewall prompts, sleep/wake, or packet loss on a physical LAN.
- DNS-SD publication; the current slice is browse/candidate-only.
- Makaretu packet-parser resilience against an independent fuzzing corpus, or
  final acceptance of its old transitive graph and package-license provenance.
- Windows or Linux runtime behavior; hosted matrix results are required for the
  committed code, and physical-machine evidence remains separate.

Those boundaries remain open in `specs/v1/tasks.md` and the v1 release criteria.
