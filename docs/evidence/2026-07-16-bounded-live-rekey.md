# Protocol 1.3 Bounded Live Rekey Evidence — 2026-07-16

## Evidence boundary

This slice adds bounded directional traffic-key evolution to Flowspan's
authenticated direct-TCP control channel. Protocol 1.3 peers exchange encrypted
`FSR1` KeyUpdate plaintexts as the final frame under an old directional key,
derive the next key with the frozen HKDF schedule, erase the retired buffer, and
resume at the next epoch with sequence zero. A full-connection request converges
both directions on one target epoch without a second rotation when requests
cross.

Protocol 1.2 retains encrypted Finished but never accepts or emits KeyUpdate. It
closes and enters the authenticated reconnect path when a directional usage
bound is exhausted. Protocol 1.0 and 1.1 remain the explicitly marked legacy
compatibility path.

This evidence does not claim post-compromise recovery, independent
cryptographic approval, physical hostile-LAN resistance, two-device behavior,
or packaged native-provider behavior. Those remain separate release blockers.

Branch: `codex/v1-foundation`

Implementation commit:
`2cf1e1fbe0ce12dc34cebc4aa449dd7c4fa65835`.

Evidence commit:
`8ee0a7d423f7326e4e7d8f37880a2fac0c150b1c`.

Peer-request race repair commit:
`369f92e32809d58b5b34f395b4968bc1e2d77309`.

The task-status commit containing the final status remains subject to the same
exact-commit hosted gates after it is pushed.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

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
dotnet list Flowspan.slnx package --vulnerable \
  --include-transitive --no-restore
git diff --check
```

## Local results

- locked restore and formatting passed;
- the warning-as-error Release build passed with 0 warnings and 0 errors;
- 794 tests passed, 0 failed, and 0 skipped:
  - Desktop: 162;
  - Transport: 219;
  - Integration: 141;
  - Security: 123;
  - Domain: 39;
  - Protocol: 28;
  - shared platform contracts: 16;
  - Windows platform contracts: 18;
  - macOS platform contracts: 16;
  - Linux platform contracts: 18;
  - mDNS transport contracts: 14;
- explicit Desktop composition printed
  `Flowspan desktop composition validation passed in explicit TEST MODE.`;
- the deterministic simulator selected protocol `1.3` and still reported
  preserved source, resumed target, and committed Atomic Swap;
- the transitive vulnerability query reported no vulnerable package in any of
  the 24 projects;
- Standards and Spec reviews each returned 0 findings; the subsequent race
  repair was checked against RK5 and changed only authenticated peer-request
  coalescing plus its deterministic regression coverage.

These are local macOS and same-host results. Platform-named contract projects do
not constitute native Windows or Linux execution; the hosted matrix below is
reported separately.

## Fresh-process repetitions

The implementation candidate ran each original group in 20 distinct
`dotnet test` processes with `--no-build` and `--no-restore`. After hosted
Windows exposed the coalescing race, the repaired tree additionally ran the two
directly affected tests together in 20 independent processes:

| Tree | Group | Filter | Per process | Result |
| --- | --- | --- | ---: | ---: |
| Implementation | Protocol | `FullyQualifiedName~ProtocolNegotiatorTests` | 7 | 20/20 |
| Implementation | Security | `SecureSessionKeyUpdateTests`, `SecureSessionTests`, `SecureSessionRekeyPropertyTests`, and `SecureSessionKeyErasureTests` | 38 | 20/20 |
| Implementation | Transport | secure-channel, fault, concurrency, repeated/crossed authenticated rekey, interrupted reconnect, and usage-bound session-end tests | 29 | 20/20 |
| Implementation | Desktop | protocol-1.2 status, complementary Capability reconnect, and production advertisement composition | 4 | 20/20 |
| Race repair | Crossed/coalesced update | `ProtocolOnePointThreeRepeatedAndCrossedRekeysKeepTrafficBound` and `LocalRekeyCoalescesWithAuthenticatedPeerRequestInProgress` | 2 | 20/20 |

The model/property trace uses four fixed seeds and performs 64 consecutive
single-initiator or crossed transitions per seed through real AEAD frames. The
concurrent-send regression starts 24 application sends together at an injected
four-frame limit and proves that the reserved KeyUpdate frame cannot be consumed
by racing callers.

## Frozen wire and key-schedule evidence

The canonical requesting KeyUpdate for target epoch two is exactly 10 bytes:

```text
46535231010100000002
```

Its SHA-256 is:

```text
919E1A6CECA322B61A0F98612E55C0584189AE166CC6685E8FB775FBDAD71F45
```

For current key `11` repeated 32 bytes, session identifier `22` repeated 16
bytes, initiator direction, and target epoch two, the frozen HKDF-SHA-256 result
is:

```text
E1CEE8A87F7D1A22645CE8968C7226F68E7A790AF3C2D07DE8C0D80B80902591
```

Tests reject wrong magic, kind, flag, length, trailing data, epoch zero/one,
replay, gaps, overflow, pending-target mismatch, and unsolicited response. A
white-box security invariant retains references to the superseded and active key
buffers and proves both are zero after rotation and disposal respectively.

## State, concurrency, and failure evidence

The secure-frame owner now maintains independently locked send and receive
epochs, sequence counters, and plaintext-byte counters. It enforces at most
1,048,576 frames and 1 GiB protected plaintext per epoch. The control channel
reserves one old-epoch frame plus the 10-byte KeyUpdate before admitting another
application message.

Observable tests prove:

- early-new and late-old frames fail without probing multiple keys;
- invalid epoch transitions do not advance reusable state;
- a valid old-epoch KeyUpdate is fully authenticated before the receiver moves
  to the next epoch;
- a malformed authenticated KeyUpdate may consume its old-epoch record sequence
  but cannot advance the key epoch; the channel is then destroyed and never
  reused;
- single, duplicate, repeated, and crossed requests converge without rollback,
  response ping-pong, or a second rotation;
- a local rekey request arriving after an authenticated peer request advances
  receive epoch `N` but before the response advances send epoch `N` coalesces
  into that target and emits one non-requesting response;
- application sends and KeyUpdate writes have a consistent lock order, while
  disposal interrupts pending reads before waiting for gates;
- KeyUpdate is flushed completely before the sender installs its next key;
- one deadline covers request write, flush, and peer response;
- pre-cancellation writes nothing and leaves the channel usable, while timeout
  or cancellation after commit destroys the channel;
- injected first write, second write, flush, receive, response-write, AEAD,
  malformed-plaintext, and cleanup failures all fail closed;
- when both a primary operation and cleanup fail, both causes are preserved.

Real authenticated loopback repeatedly alternates unilateral and simultaneous
updates while carrying identity/version-bound application messages after each
epoch. A separate real-TCP recovery test interrupts a committed update, proves
the old receive/rekey/send paths are unusable, then performs a fresh signed
handshake, verifies both directions restart at epoch and sequence one, and
carries a new bound control message.

The new coalescing regression was first run against the unmodified production
path and failed 1/1 with `InvalidOperationException` at the matching-epoch
precondition. It passed 1/1 after the minimal state repair, then passed together
with the deterministic real-TCP crossed-request path in all 20 fresh processes.

## Production compatibility evidence

The production version profile contains protocol 1.0, 1.1, 1.2, and 1.3;
highest-common negotiation prefers 1.3. The Desktop inbound listener,
advertisement, trusted reconnect loop, and simulator consume the same immutable
profile. The authenticated control connection enables live rekey only when its
signed negotiated version supports protocol 1.3.

Protocol 1.2 tests prove encrypted Finished still advances initial frame
counters and that the public rekey entry point is rejected. A reduced-limit
legacy channel emits only decodable application frames, never `FSR1`, then
faults at the bound. The peer-session attempt classifies that post-authentication
cryptographic usage-bound failure as a completed session so the existing bounded
reconnect supervisor can establish a fresh authenticated connection. Desktop
status separately names 1.2 as `RECONNECT-AT-KEY-LIMIT`; 1.0/1.1 retain their
stronger `LEGACY COMPATIBILITY` warning.

## Hosted exact-commit evidence

Implementation commit `2cf1e1fbe0ce12dc34cebc4aa449dd7c4fa65835`
passed [CI run `29499039781`](https://github.com/happys2333/flowspan/actions/runs/29499039781):

- Ubuntu job [`87623003035`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003035);
- Windows job [`87623003077`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003077);
- macOS job [`87623003084`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003084);
- Secret Scan job [`87623003088`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003088).

Every OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.3 simulator, and uploaded test evidence.

[CodeQL run `29499039757`](https://github.com/happys2333/flowspan/actions/runs/29499039757),
job [`87623003224`](https://github.com/happys2333/flowspan/actions/runs/29499039757/job/87623003224),
also passed for the exact commit. CodeQL 2.26.0 scanned 196/196 C# files,
evaluated 52 rules, uploaded analysis `1487268209`, and reported 0 results and 0
open alerts.

Downloaded test artifacts were:

| OS | Artifact | SHA-256 digest | TRX files |
| --- | ---: | --- | ---: |
| Windows | `8375680254` | `b40037d1214cb67259c5d9e609275d17b2902f7d69eb701de01b7694cdc3f440` | 11 |
| macOS | `8375667668` | `9bdcbe647405c9aaebb2e9f4ec35eba2ec913e617ec8e86e9e5e2d1f7ad968be` | 11 |
| Linux | `8375661055` | `fa32b6e82804b88dcd4c56032368bc632c2fade4f5c5a4a10fe90990797f29d1` | 11 |

Independently summing every downloaded TRX `Counters` element produced the same
result on each OS:

```text
total=793 executed=793 passed=793 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 inProgress=0 pending=0
```

Evidence commit `8ee0a7d423f7326e4e7d8f37880a2fac0c150b1c` then exposed a
real scheduling race in [CI run `29499549954`](https://github.com/happys2333/flowspan/actions/runs/29499549954).
Ubuntu job [`87624721058`](https://github.com/happys2333/flowspan/actions/runs/29499549954/job/87624721058),
macOS job [`87624721133`](https://github.com/happys2333/flowspan/actions/runs/29499549954/job/87624721133),
and Secret Scan job [`87624721103`](https://github.com/happys2333/flowspan/actions/runs/29499549954/job/87624721103)
passed, while Windows job [`87624721147`](https://github.com/happys2333/flowspan/actions/runs/29499549954/job/87624721147)
failed `ProtocolOnePointThreeRepeatedAndCrossedRekeysKeepTrafficBound`.

The failure occurred in the authenticated interval after one receive direction
had advanced to `N+1` but before its response advanced the local send direction.
A concurrent local `RekeyAsync` incorrectly rejected that legitimate
`send=N, receive=N+1` state. The base implementation run had not scheduled this
window, so its earlier green result was not treated as sufficient after the
Windows evidence. The evidence commit's [CodeQL run `29499548184`](https://github.com/happys2333/flowspan/actions/runs/29499548184)
still passed, but the failed CI gate remained authoritative.

Race repair commit `369f92e32809d58b5b34f395b4968bc1e2d77309`
passed [CI run `29500786183`](https://github.com/happys2333/flowspan/actions/runs/29500786183):

- Windows job [`87628871504`](https://github.com/happys2333/flowspan/actions/runs/29500786183/job/87628871504);
- Ubuntu job [`87628871505`](https://github.com/happys2333/flowspan/actions/runs/29500786183/job/87628871505);
- macOS job [`87628871542`](https://github.com/happys2333/flowspan/actions/runs/29500786183/job/87628871542);
- Secret Scan job [`87628871532`](https://github.com/happys2333/flowspan/actions/runs/29500786183/job/87628871532).

[CodeQL run `29500784923`](https://github.com/happys2333/flowspan/actions/runs/29500784923),
job [`87628866883`](https://github.com/happys2333/flowspan/actions/runs/29500784923/job/87628866883),
passed for the repair commit. CodeQL 2.26.0 evaluated 52 rules, uploaded analysis
`1487384139`, and reported 0 results and 0 open alerts for the 196-file C# tree.

The repair artifacts were:

| OS | Artifact | SHA-256 digest | TRX files |
| --- | ---: | --- | ---: |
| Windows | `8376398057` | `09c32b37285f3ac8bf2f5e83c33e17015a193f6b4bb1d2d599c703f4abd9810b` | 11 |
| macOS | `8376366944` | `985b1d35602d8a79b87580f7c5e45f2af00fd1703cda930e342d33de6b25ad4e` | 11 |
| Linux | `8376368271` | `56e8c457f5820b2da87ea76837e622a9c94e4f832b66f3d614d1001f91148f58` | 11 |

Independently summing every downloaded repair TRX `Counters` element produced
the same result on each OS:

```text
total=794 executed=794 passed=794 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 passedButRunAborted=0 notRunnable=0
notExecuted=0 disconnected=0 warning=0 inProgress=0 pending=0
```

This closes the automated implementation gate with exact downloaded test
records rather than inferring it only from workflow badges.

## Remaining limits

- The one-way HKDF traffic-key chain erases past keys but does not provide
  post-compromise recovery; fresh authenticated reconnect is that boundary.
- Independent cryptographic/security review remains mandatory before the v1
  security release gate can close.
- Same-host loopback and hosted runners do not prove physical hostile-LAN,
  process-memory, firewall, sleep/wake, or two-device behavior.
- Mobile platforms remain outside the approved v1 scope.

Therefore the automated implementation and evidence scope of task 4.3b is
complete. The task-status commit containing that status must still pass its own
exact-commit hosted consistency gates. This does not close parent task 4.3,
task 4.1, the independent cryptographic review, the physical two-device gates,
the v1 security release gate, or Flowspan v1.
