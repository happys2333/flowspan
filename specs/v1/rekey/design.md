# Protocol 1.3 Live Rekey Design

Status: proposed for task 4.3b

## Design summary

Protocol 1.3 keeps the existing authenticated handshake and encrypted Finished,
then evolves the two independent traffic keys toward one shared target epoch. A
KeyUpdate is an internal encrypted control plaintext, not an Activity
`ControlMessage`. It is the final frame under the old directional key; the next
frame in that direction uses the target epoch at sequence zero.

This follows the public TLS 1.3 KeyUpdate safety shape—old-key update record,
independent directional traffic secrets, and reject-new-before-update—while
using a clean-room Flowspan wire format and key schedule. RFC 8446 sections
4.6.3, 5.5, and 7.2 are design evidence, not implementation source.

## Version and compatibility

- Protocol 1.3 is the first version that supports live rekey.
- Protocol 1.2 still requires bidirectional encrypted Finished but has no
  KeyUpdate. It closes and reconnects before the key-usage bound.
- Protocol 1.0/1.1 remain the explicitly degraded legacy compatibility path.
- The production profile advertises `1.3, 1.2, 1.1, 1.0` only after the full
  1.3 implementation and compatibility tests are present.

## Wire format

The KeyUpdate plaintext is exactly 10 bytes and is protected by the ordinary
`FSE1` frame for the sender's current epoch and sequence:

```text
4 bytes magic "FSR1"
u8 kind = 1
u8 flags: bit 0 requests peer update; all other bits zero
u32 next_epoch, big-endian
```

`next_epoch` must equal the receiver's current receive epoch plus one and be at
least two. Unknown kind/flag bits, trailing data, zero/one epoch, overflow, or an
epoch gap is fatal.

No operation ID is needed: each direction has one strictly ordered epoch chain,
and the channel permits at most one locally awaited peer-update request. AEAD
associated data already binds the session identifier, direction, old epoch,
sequence, and ciphertext length.

## Key schedule

Each `SecureFrameProtector` owns one 32-byte directional traffic key, current
`uint32` epoch, and current `uint64` sequence. Epoch one continues to use the
handshake-derived key frozen in ADR 0003. For `nextEpoch = currentEpoch + 1`:

```text
salt = session identifier (16 bytes)
info = UTF8("FLOWSPAN-REKEY-V1")
       || u8(direction)
       || u32_big_endian(nextEpoch)
nextKey = HKDF-SHA256(currentKey, salt, info, 32 bytes)
```

The implementation derives into a new buffer, erases the old key, installs the
new key, advances the epoch, and resets sequence to zero while holding the
protector lock. A derivation failure leaves the current protector unchanged but
faults and disposes the owning channel. Epoch or sequence never wraps.

The scheme provides forward erasure of superseded keys; it does not claim
post-compromise security. A fresh authenticated reconnect supplies new ECDH
material after any interrupted transition.

## Usage bound

Flowspan limits each directional key to at most 1,048,576 encrypted frames and
1 GiB of protected application plaintext. The default protocol-1.3 channel
initiates KeyUpdate before application traffic would exceed either threshold.
One bounded KeyUpdate frame is reserved as transition overhead. Tests inject
small limits through an internal profile; production callers cannot raise them.

This is deliberately below the roughly `2^24.5` full-size-record AES-GCM bound
discussed by RFC 8446 section 5.5. Flowspan permits plaintexts up to 256 KiB, so
the lower `2^20` frame limit plus the byte limit preserves additional margin for
Flowspan's larger maximum plaintext.

## State and concurrency

`SecureFrameSession` exposes thread-safe sender/receiver epoch and sequence
snapshots plus atomic `AdvanceSendEpoch` and `AdvanceReceiveEpoch` operations.
The two protectors remain independent.

`SecureControlChannel` continues to serialize writes and reads with its existing
gates:

1. `SendAsync` acquires the send gate. If the usage threshold would be exceeded,
   it starts or joins one full-connection update, writes a requesting KeyUpdate
   under the old key, flushes it, advances the sender, then writes the
   application message under the new key.
2. `ReceiveAsync` acquires the receive gate and loops. It decrypts exactly under
   the current receive epoch. If plaintext is KeyUpdate, it validates the
   message, advances the receiver, signals any satisfied local request, sends a
   response when required, and continues until an application message is ready.
3. `RekeyAsync` permits one pending local target epoch. It sends a requesting
   KeyUpdate, advances the sender, and waits for `ReceiveAsync` to observe the
   peer reaching that epoch within the configured deadline. A live production
   control session already owns one continuous receive loop.

When a peer requests target epoch `N`:

- if `localSendEpoch == N - 1`, send one non-requesting KeyUpdate to `N`;
- if `localSendEpoch >= N`, a crossed or earlier local update already satisfies
  the request, so do not rotate again;
- if `localSendEpoch < N - 1`, fault the channel as an epoch gap.

Thus simultaneous requests for the same next epoch cross safely and each
direction advances once. A frame already holding the send gate may finish under
the old epoch before the response; ordered TCP keeps it before the response
KeyUpdate.

```mermaid
sequenceDiagram
    participant A as "Peer A"
    participant B as "Peer B"
    A->>B: "KeyUpdate(next=2, request=1) under A epoch 1"
    Note over A: "A send becomes epoch 2"
    Note over B: "B receive becomes epoch 2"
    B->>A: "KeyUpdate(next=2, request=0) under B epoch 1"
    Note over B: "B send becomes epoch 2"
    Note over A: "A receive becomes epoch 2; request completes"
```

## Failure and recovery

Any malformed KeyUpdate, epoch mismatch, AEAD failure, key derivation failure,
post-send cancellation, response timeout, EOF, or transport error faults the
channel, erases owned keys, and closes the stream. A pre-cancelled request that
has not written anything leaves the channel unchanged. The reconnect supervisor
then creates a fresh TCP connection, signed handshake, Finished exchange, and
epoch-one session. No rekey journal or cross-connection resume token exists.

## Module changes

- `Flowspan.Protocol`: protocol-1.3 feature gate only; KeyUpdate is below the
  Activity control-message layer.
- `Flowspan.Security`: canonical KeyUpdate value/codec, epoch key derivation,
  mutable directional protector epochs, limits, and state-transition tests.
- `Flowspan.Transport`: KeyUpdate multiplexing, request deadline/coalescing,
  automatic limit transition, socket failure semantics, and loopback tests.
- `Flowspan.Desktop` and simulator: advertise 1.3 only after production
  integration is complete; retain explicit lower-version presentation.

## Verification matrix

- Golden 10-byte KeyUpdate fixture/hash and next-key derivation vector.
- Model/property traces for two peers across repeated and simultaneous updates.
- Wrong flag/kind/length/epoch; replay, gap, early-new, late-old;
  AEAD tamper without state advance.
- Threshold boundary, epoch/sequence exhaustion, old-key erasure, repeated
  update, duplicate local request, and cancellation before/after write.
- Send, flush, receive, response, and cleanup fault injection.
- Protocol 1.2 compatibility/limit-close and protocol 1.3 preference.
- Real TCP loopback with application messages before and after multiple rekeys,
  plus reconnect after an interrupted update.

## Security limits

The format remains provisional until independent cryptographic review. Hosted
runner and loopback evidence do not prove physical hostile-LAN behavior or
process-memory resistance. Key chaining improves past-key erasure but does not
heal a currently compromised endpoint; reconnect is the recovery boundary.
