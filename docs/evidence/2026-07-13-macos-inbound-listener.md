# Evidence: macOS authenticated inbound listener, 2026-07-13

Classification: **Local**, **loopback integration**, and **simulated/contract**

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
- 246 tests passed, 0 failed, and 0 skipped;
  - security: 50 passed, including the claimed-Device-ID/key-substitution
    negative;
  - transport: 65 passed, including all 9 inbound-listener tests;
- the simulator negotiated protocol 1.0, preserved the source, resumed the
  target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

## What this proves

- One real `TcpListener` on the local loopback interface authenticated two
  different currently trusted identities through the same port and ran both
  handlers concurrently.
- The Device ID claimed by the first hello is used only to select current trust.
  A different identity using that same ID is rejected by the hello identity
  check, while the authenticated handshake separately verifies trusted-key
  possession.
- An unknown peer is closed and reported without preventing the next trusted
  peer from connecting.
- The listener rechecks current trust and the authenticated key immediately
  before capability registration, and denies a peer missing the required grant.
- The default inbound handshake/session slot limit is 32, configuration above
  the hard limit of 128 is rejected, and a one-slot test observes backpressure.
- Handler failure is isolated to its peer. Peer revocation drains only that
  peer, while caller cancellation or a fatal accept failure cancels and awaits
  all active sessions.
- Failures observed in the negative tests are classified into authentication,
  authorization, or handler diagnostic stages.

## What this does not prove

- No second process, second device, non-loopback interface, multicast socket,
  firewall rule, VPN, sleep/wake transition, or network-interface change was
  involved.
- The test does not prove that Windows or Linux TCP/network providers behave the
  same way; matching hosted CI is required for this source state.
- The test does not validate physical DNS-SD publication/discovery or connection
  through a discovered LAN address.
- The provisional handshake has not received independent security review and
  this evidence does not make it a frozen production protocol.

Physical two-device LAN and native platform evidence remain open in the v1 task
tracker and release criteria.
