# Protocol 1.2 Encrypted Finished Evidence — 2026-07-16

## Evidence boundary

This slice adds explicit bidirectional key confirmation to the authenticated
direct-TCP handshake. When both peers negotiate protocol 1.2, each must send an
epoch-1, sequence-zero AEAD frame whose canonical Finished plaintext binds its
role, the signed handshake transcript hash, and the derived session identifier.
The connection is not exposed as a control channel until both frames verify.

Protocol 1.0 and 1.1 remain a deliberate compatibility path without explicit
Finished. Live rekey, independent cryptographic review, physical interception,
two-device networking, and packaged native-provider evidence remain separate
release blockers. This slice does not claim those gaps are closed.

Branch: `codex/v1-foundation`

Implementation commit: `2bcc05cd0e49bbdbba787bcc0a493961e4da2656`.

Evidence commit: `fd73fa74cb60db026e0e3effa915d23abd0c3f48`.

Task 4.3a becomes final when its task-status commit passes Windows, macOS, and
Ubuntu CI, Secret Scan, and CodeQL.

## Local environment and commands

```text
Host: macOS 26.5.2 (build 25F84), Apple Silicon, Asia/Shanghai
.NET SDK: 10.0.301
.NET runtime: 10.0.9
MSBuild: 18.6.4
RID: osx-arm64
```

```sh
dotnet restore Flowspan.slnx --locked-mode --nologo
dotnet format Flowspan.slnx --no-restore
dotnet format Flowspan.slnx --verify-no-changes --no-restore \
  --verbosity minimal
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

- formatting and the warning-as-error Release build passed with 0 warnings and
  0 errors;
- 734 tests passed, 0 failed, and 0 skipped:
  - Desktop: 161;
  - Transport: 192;
  - Integration: 141;
  - Security: 93;
  - Domain: 39;
  - Protocol: 26;
  - shared platform contracts: 16;
  - Windows platform contracts: 18;
  - macOS platform contracts: 16;
  - Linux platform contracts: 18;
  - mDNS transport contracts: 14;
- the deterministic simulator selected protocol `1.2` and still reported
  preserved source, resumed target, and committed Atomic Swap.
- explicit desktop composition validation passed in TEST MODE;
- the transitive dependency vulnerability query reported no vulnerable package
  in any of the 24 projects.

The Standards and Spec reviews each returned 0 findings on the final local tree.

## Final-tree fresh-process repetitions

Each row ran the following command shape in 20 separate `dotnet test` processes:

```sh
for iteration in {1..20}; do
  dotnet test <project> --configuration Release --no-build --no-restore \
    --filter "<filter>"
done
```

| Group | Filter | Per process | Result |
| --- | --- | ---: | ---: |
| Protocol | `FullyQualifiedName~ProtocolNegotiatorTests` | 5 | 20/20 |
| Security | `FullyQualifiedName~AuthenticatedSessionHandshakeTests\|FullyQualifiedName~SecureSessionTests` | 18 | 20/20 |
| Transport | `FullyQualifiedName~AuthenticatedSessionFinishedExchangeTests\|FullyQualifiedName~AuthenticatedTcpControlConnectionTests\|FullyQualifiedName~ProtocolOnePointTwoInvalidInitiatorFinishedNeverRunsHandler` | 18 | 20/20 |
| Desktop | `FullyQualifiedName~LegacyAuthenticatedSessionNamesDegradedSecurityMode\|FullyQualifiedName~ProductionLoopAuthenticatesEitherOneWayCapabilityDirection\|FullyQualifiedName~SuccessfulStartAdvertisesBoundPortAndDisposeWithdrawsEverything` | 5 | 20/20 |

These repetitions are fresh-process same-host evidence. They do not replace the
pending hosted OS matrix or physical two-device testing.

## Canonical and cryptographic evidence

`ProtocolFeatures.RequiresSecureSessionFinished` gates Finished at protocol 1.2
within major version 1. Both version lists and the highest common selection are
already signed in the authenticated transcript, so changing a mutually offered
1.2 selection invalidates the identity signature.

The frozen Finished plaintext is 62 bytes. With initiator role, transcript hash
`11` repeated 32 bytes, and session identifier `22` repeated 16 bytes, its
SHA-256 is:

```text
FD15E6104A00DCB7F7809FE39B71BBB9DA3F673A511DC3EB6F77F7ED7068BDAF
```

Tests compare the complete encoded bytes as well as this hash. They reject
unknown roles, shortened fields, trailing data, role substitution, transcript
substitution, and session substitution.

The canonical plaintext is encrypted by the existing directional `FSE1`
protector. A tampered tag fails without advancing the receive counter; the valid
frame then verifies and advances both the sender and receiver from sequence zero
to one. Thus the first control frame cannot reuse the Finished nonce.

## TCP state-machine evidence

The protocol-1.2 initiator sends Finished and then receives the responder's
Finished. The responder verifies the initiator before replying. Both use the
same whole-handshake deadline and dispose derived keys plus the socket on any
failure.

Real loopback tests prove:

- a 1.2 connection is returned only with all four directional counters at one;
- omission of the responder Finished times out before control upgrade;
- a tampered responder Finished returns structured `InvalidPeerFinished` and no
  connection;
- omission, tamper, or valid-ciphertext binding mismatch from an initiator makes
  the production inbound responder close its socket before Trust registration
  or handler invocation;
- 1.1 still completes the four signed messages with counters at zero;
- the production desktop discovery/reconnect composition advertises and
  negotiates 1.2 for both complementary one-way Capability directions under its
  deterministic smaller-Device-ID connector election.

Deterministic Finished-transaction fault tests additionally inject both roles'
send failure and both roles' missing, tampered, and wrongly bound receive. Every
case proves the authenticated frame session becomes unusable and the transport
is disposed; a simultaneous cleanup failure preserves both exceptions. A
desktop state test proves a negotiated 1.0 or 1.1 session is explicitly
labelled `LEGACY COMPATIBILITY` rather than sharing protocol 1.2's status.

The implementation was developed through observable RED/GREEN checks: the
feature gate, canonical value/codec, session-identifier export, pre-upgrade
sequence assertion, and production 1.2 advertisement each failed before its
minimal behavior was added.

## Hosted exact-commit evidence

Implementation commit `2bcc05cd0e49bbdbba787bcc0a493961e4da2656`
passed [CI run `29493934859`](https://github.com/happys2333/flowspan/actions/runs/29493934859):

- macOS job [`87606341153`](https://github.com/happys2333/flowspan/actions/runs/29493934859/job/87606341153);
- Windows job [`87606341162`](https://github.com/happys2333/flowspan/actions/runs/29493934859/job/87606341162);
- Ubuntu job [`87606341306`](https://github.com/happys2333/flowspan/actions/runs/29493934859/job/87606341306);
- Secret Scan job [`87606341168`](https://github.com/happys2333/flowspan/actions/runs/29493934859/job/87606341168).

Each OS job restored locked dependencies, verified formatting, built with
warnings as errors, ran all tests, validated Desktop composition in explicit
TEST MODE, ran the protocol-1.2 simulator, and uploaded test evidence.
[CodeQL run `29493934922`](https://github.com/happys2333/flowspan/actions/runs/29493934922),
job [`87606341332`](https://github.com/happys2333/flowspan/actions/runs/29493934922/job/87606341332),
also passed for the same commit, scanned 189/189 C# files, and uploaded the
result.

Downloaded test artifacts were Windows `8373631228`, Linux `8373607574`, and
macOS `8373607515`. Each contains 11 TRX files. Independently summing their
`Counters` attributes produced the same result on every OS:

```text
total=734 executed=734 passed=734 failed=0 error=0 timeout=0
aborted=0 inconclusive=0 notExecuted=0
```

The implementation gate is therefore proved by downloaded test records rather
than inferred only from a green workflow badge. Hosted runners remain CI
evidence, not physical two-device or independent cryptographic-review evidence.

Evidence commit `fd73fa74cb60db026e0e3effa915d23abd0c3f48` also passed
[CI run `29494260925`](https://github.com/happys2333/flowspan/actions/runs/29494260925):

- macOS job [`87607380333`](https://github.com/happys2333/flowspan/actions/runs/29494260925/job/87607380333);
- Windows job [`87607380575`](https://github.com/happys2333/flowspan/actions/runs/29494260925/job/87607380575);
- Ubuntu job [`87607380330`](https://github.com/happys2333/flowspan/actions/runs/29494260925/job/87607380330);
- Secret Scan job [`87607380282`](https://github.com/happys2333/flowspan/actions/runs/29494260925/job/87607380282).

[CodeQL run `29494260905`](https://github.com/happys2333/flowspan/actions/runs/29494260905),
job [`87607379994`](https://github.com/happys2333/flowspan/actions/runs/29494260905/job/87607379994),
passed for the evidence commit, scanned 189/189 C# files, and uploaded the
result. Downloaded Windows artifact `8373749877`, Linux artifact `8373728011`,
and macOS artifact `8373738334` each contained 11 TRX files; each OS
independently summed to 734 total, executed, and passed with zero failed, error,
timeout, aborted, inconclusive, or not-executed results.

## Remaining limits

- Protocol 1.0/1.1 compatibility is detectable by negotiated version but does
  not provide explicit Finished evidence.
- No live epoch rotation or simultaneous-rekey state machine exists; task 4.3b
  remains open.
- The format and implementation remain provisional pending independent
  cryptographic review.
- Same-process loopback and hosted runners do not prove resistance on a
  physically hostile LAN, process-memory compromise, or native provider defects.

Therefore this local evidence does not close parent task 4.3, task 4.1, the v1
security release gate, or Flowspan v1.
