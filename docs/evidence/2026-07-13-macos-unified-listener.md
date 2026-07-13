# Evidence: macOS unified TCP listener, 2026-07-13

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
- 287 tests passed, 0 failed, and 0 skipped;
  - security: 82 passed, including 10 initial-family selector cases and the
    pairing-registration/concurrent-revocation ordering test;
  - transport: 74 passed, including 8 unified-listener tests;
- the simulator negotiated protocol 1.0, preserved the source, resumed the
  target, and exited successfully;
- NuGet reported no known vulnerable direct or transitive package in any
  project.

## What this proves

- The first bounded frame selects pairing only for `FSP1`, kind 1, or an
  authenticated session only for `FSH1`, kind 1. Truncated, wrong-kind, unknown,
  oversized, and 512 seeded hostile selector inputs stay inside the format-error
  contract.
- The pre-read first frame is transferred with one connection owner to exactly
  one selected decoder. Pairing cannot reuse its unauthenticated socket as a
  control channel.
- One real loopback listener completed the pairing ceremony, closed that socket,
  and authenticated the same peer over a new connection to the same published
  port before invoking the control-session handler.
- A pending pairing decision does not block an already trusted peer. Pairing,
  and authenticated-session capacity independently rejects excess work without
  writing Trust, while the hard total limit bounds accepted concurrent work.
- Protocol selection uses the injected deadline. Unknown-family failure is
  isolated so the next trusted peer connects, while cancellation and a fatal
  accept failure cancel and await active pairing/session work.
- Pairing registration and concurrent revocation share the coordinator lock. In
  the injected ordering test, revocation waits for registration and then wins,
  leaving no Trust Record.

## What this does not prove

- All TCP traffic remained inside one process and one host. No physical LAN,
  firewall, discovered address, second process, or second device was involved.
- Pairing decisions and session handlers were deterministic test doubles. No
  desktop prompt, human SAS comparison, accessibility behavior, accidental
  approval prevention, or sharing indicator was exercised.
- No Windows or Linux network provider ran locally. Matching hosted CI for
  commit `8e2a410` is recorded in
  [the hosted CI evidence](2026-07-13-hosted-ci.md); those jobs remain
  loopback/contract rather than physical-device evidence.
- The provisional selector, pairing, and authenticated-handshake formats have
  not received independent cryptographic or protocol review.
- A same-port listener does not by itself prove physical DNS-SD publication,
  discovery-to-connect composition, sleep/wake recovery, or interface churn.

Those native, physical, UI, and review claims remain open in the v1 task tracker
and release criteria.
