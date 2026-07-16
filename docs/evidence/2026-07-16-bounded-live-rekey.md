# Protocol 1.3 Bounded Live Rekey Evidence — 2026-07-16

## Evidence boundary

This slice adds protocol-1.3 directional traffic-key rotation to the existing
authenticated direct-TCP control channel. A canonical encrypted KeyUpdate is the
last frame under the retiring directional key. The next frame uses the derived
key, the next epoch, and sequence zero. A full-connection request asks both
directions to converge on the same target epoch without treating the two
directions as one atomic cryptographic state.

Protocol 1.2 retains bidirectional encrypted Finished and closes at its
traffic-key usage bound so the reconnect supervisor can establish a fresh
authenticated session. Protocol 1.0 and 1.1 remain explicitly labelled legacy
compatibility. This slice does not claim arbitrary application-process
migration, post-compromise recovery, cross-connection rekey resume, independent
cryptographic review, or physical two-device hostile-LAN evidence.

Branch: `codex/v1-foundation`

Implementation commit: `2cf1e1fbe0ce12dc34cebc4aa449dd7c4fa65835`.

Evidence commit: pending.

Task 4.3b becomes final only after the evidence and task-status commits pass the
same Windows, macOS, Ubuntu, Secret Scan, and CodeQL gates.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4.27133
RID: osx-arm64
```

The final implementation tree was clean relative to the implementation commit.
It ran:

```sh
dotnet format Flowspan.slnx --verify-no-changes --no-restore
dotnet build Flowspan.slnx --configuration Release --no-restore
dotnet test Flowspan.slnx --configuration Release \
  --no-build --no-restore \
  --logger "trx;LogFilePrefix=local-rekey" \
  --results-directory TestResults/local-rekey
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

- formatting and the warning-as-error Release build passed with 0 warnings and
  0 errors;
- 793 tests passed, 0 failed, and 0 skipped:
  - Desktop: 162;
  - Transport: 218;
  - Integration: 141;
  - Security: 123;
  - Domain: 39;
  - Protocol: 28;
  - shared platform contracts: 16;
  - Windows platform contracts: 18;
  - macOS platform contracts: 16;
  - Linux platform contracts: 18;
  - mDNS transport contracts: 14;
- explicit Desktop composition validation passed in TEST MODE;
- the deterministic simulator selected protocol `1.3` and reported preserved
  source, resumed target, and committed Atomic Swap;
- the transitive dependency query reported no vulnerable package in any of the
  24 projects;
- `git diff --check` passed.

The final Standards and Spec reviews each returned 0 findings. The review fixed
three pre-commit gaps before reaching that result: usage-bound cryptographic
failures now return an authenticated-session-end outcome to reconnect logic;
the specification distinguishes invalid `FSE1` headers from a malformed
KeyUpdate inside an already authenticated old-epoch record; and real TCP
loopback now proves interrupted-rekey recovery through a fresh handshake.

## Final-tree fresh-process repetitions

Each row ran its filter in 20 independent `dotnet test` processes. Every group
completed 20/20:

| Group | Filter | Tests per process | Result |
| --- | --- | ---: | ---: |
| Security state and erasure | `SecureSessionKeyUpdateTests`, `SecureSessionRekeyPropertyTests`, `SecureSessionKeyErasureTests` | 21 | 20/20 |
| Secure control channel | `SecureControlChannel` | 25 | 20/20 |
| Authenticated TCP | `AuthenticatedTcpControlConnectionTests` | 8 | 20/20 |
| Production profile and presentation | protocol-1.2 status, production loop, production offer | 4 | 20/20 |

These repetitions are same-host scheduler, loopback, and contract evidence. They
do not substitute for the hosted OS matrix or physical two-device testing.

## Canonical cryptographic evidence

The KeyUpdate plaintext is exactly 10 bytes:

```text
4 bytes "FSR1"
u8 kind = 1
u8 flags, bit 0 requests peer update
u32 next epoch, big-endian
```

The frozen request for epoch two is:

```text
46535231010100000002
```

Its SHA-256 is:

```text
919E1A6CECA322B61A0F98612E55C0584189AE166CC6685E8FB775FBDAD71F45
```

The next-key KDF is HKDF-SHA-256 over the current directional key, salted by the
16-byte session identifier and domain-separated by `FLOWSPAN-REKEY-V1`, traffic
direction, and next epoch. The frozen epoch-two key vector is:

```text
E1CEE8A87F7D1A22645CE8968C7226F68E7A790AF3C2D07DE8C0D80B80902591
```

Reflection-based ownership tests retain references to the old and active
directional key arrays. They prove a successful rotation zeroes the old array
before exposing the next epoch and disposal zeroes the active array. This is a
managed-buffer ownership check, not a claim that runtime, swap, crash-dump, or
kernel copies cannot exist.

## State-machine and fault evidence

The secure-frame owner enforces at most 1,048,576 frames and 1 GiB of plaintext
per direction and epoch. Protocol 1.3 reserves one old-epoch frame and the
10-byte transition plaintext before allowing more application data. Reduced
limits prove both frame and byte boundaries without weakening production limits.

Seeded traces perform repeated unilateral and crossed requests through epoch 65.
They prove both peers converge, each direction rotates once, and simultaneous
requests do not cause response ping-pong or a second rotation. Additional tests
cover duplicate local request coalescing, early-new and late-old frames, replay,
sequence and epoch gaps, malformed magic/kind/flags/length/epoch, unsolicited
responses, counter exhaustion, and legacy readers receiving `FSR1`.

The deterministic transport fault matrix covers:

- pre-cancel before any write, with epoch one and a reusable channel preserved;
- cancellation or timeout after KeyUpdate commit;
- a deadline spanning the KeyUpdate write, flush, and peer-response wait;
- first write, frame write, flush, receive, EOF, decode, AEAD authentication,
  response write, and cleanup failure;
- primary plus cleanup exception preservation;
- disposal racing blocked receive and peer-response write without lock-order
  deadlock;
- session-key destruction and rejection of every operation after a fault.

An authenticated protocol-1.3 TCP loopback carries identity/version-bound
control traffic across repeated unilateral and simultaneous rekeys through
epoch four. A separate real loopback interrupts a committed rekey, proves the
old channel cannot send again, then establishes a new signed handshake at epoch
one and carries a fresh control message.

For protocol 1.2, a reduced-bound channel proves no KeyUpdate is emitted and the
channel faults instead of exceeding its limit. The authenticated-session attempt
maps that cryptographic bound failure to `AuthenticatedSessionEnded`; the
reconnect-supervisor contract immediately creates a new attempt with its failure
counter reset. This is deterministic component evidence rather than a real
2^20-frame exhaustion run.

## Production profile and compatibility evidence

`ProtocolFeatures.ProductionSupportedVersions` contains canonical protocol
versions 1.0, 1.1, 1.2, and 1.3. Highest-common-version negotiation selects 1.3.
Desktop discovery, inbound authentication, trusted reconnect, and the simulator
use this same profile.

Desktop snapshots distinguish three states without claiming Activity sharing:

- 1.3: encrypted channel with bounded live rekey;
- 1.2: encrypted Finished with `RECONNECT-AT-KEY-LIMIT`;
- 1.0/1.1: `LEGACY COMPATIBILITY` and degraded security without encrypted
  Finished.

Production loopback proves the elected connector and shared listener both
negotiate 1.3 for either complementary one-way Activity Capability direction.
Protocol 1.2 remains directly interoperable and rejects `RekeyAsync`.

## Hosted exact-commit evidence

Implementation commit `2cf1e1fbe0ce12dc34cebc4aa449dd7c4fa65835`
passed [CI run `29499039781`](https://github.com/happys2333/flowspan/actions/runs/29499039781):

- Ubuntu job [`87623003035`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003035);
- Windows job [`87623003077`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003077);
- macOS job [`87623003084`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003084);
- Secret Scan job [`87623003088`](https://github.com/happys2333/flowspan/actions/runs/29499039781/job/87623003088).

Each OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.3 simulator, and uploaded test records.
[CodeQL run `29499039757`](https://github.com/happys2333/flowspan/actions/runs/29499039757),
job [`87623003224`](https://github.com/happys2333/flowspan/actions/runs/29499039757/job/87623003224),
also passed for the exact implementation commit.

Downloaded artifacts were Windows `8375680254`, macOS `8375667668`, and Linux
`8375661055`. Each contains 11 TRX files. Independently summing the `Counters`
attributes produced the same result for every OS:

```text
total=793 passed=793 failed=0 notExecuted=0
```

The implementation gate is therefore backed by downloaded test records rather
than inferred only from workflow badges. Hosted runners are automated contract
evidence; they are not physical Windows/macOS/Linux device testing.

## Remaining limits

- Independent cryptographic/security review has not approved the KeyUpdate
  format, KDF, limits, state machine, or implementation.
- No physical two-device session has exercised interruption, simultaneous
  update, or a hostile LAN on Windows, macOS, or Linux.
- Key chaining erases retired owned buffers but does not provide post-compromise
  recovery; a fresh authenticated handshake is the recovery boundary.
- A partially completed rekey is never resumed across disconnect or restart.
- Hosted CI and same-host loopback do not prove packaged credential providers,
  native permissions, accessibility, or physical network behavior.

Therefore this evidence closes only the implementation and automated-evidence
portion of task 4.3b. It does not close parent task 4.3, the independent security
gate, the physical-device release gates, or Flowspan v1.
