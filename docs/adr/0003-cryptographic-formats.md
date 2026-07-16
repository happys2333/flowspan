# ADR 0003: Device identity, pairing, key derivation, and encrypted frame formats

- Status: Provisional; implemented for review and interoperability tests, not
  approved for production use
- Date: 2026-07-13
- Review gate: independent cryptographic/security review required before v1

## Context

Flowspan requires interactive device pairing, authenticated reconnects, and
application-layer end-to-end encryption that remains independent of a future
byte-forwarding relay. The formats must use primitives available consistently
from the .NET base class library on Windows, macOS, and Linux.

This ADR deliberately freezes the first testable format. It does not claim that
the protocol has received independent review, and it does not authorize use of
in-memory private keys in a released build.

## Primitive choices

- Long-lived identity: ECDSA over NIST P-256 with SHA-256.
- Ephemeral agreement: ECDH over NIST P-256.
- Public-key encoding: DER SubjectPublicKeyInfo (SPKI).
- Private-key encoding at the secret-store boundary: DER PKCS#8. Plain PKCS#8
  may exist only inside process memory and test fixtures; platform storage must
  protect it at rest.
- ECDSA wire signature: 64-byte IEEE P1363 fixed-field `r || s`.
- Transcript and fingerprint hash: SHA-256.
- Session KDF: HKDF-SHA-256.
- Payload AEAD: AES-256-GCM with a 16-byte authentication tag.

P-256 is selected over newer curves because .NET and the three target OS crypto
providers expose consistent ECDSA/ECDH/SPKI/PKCS#8 support. This is a portability
decision, not a claim that P-256 is uniquely preferable.

## Device identity

`DeviceId` is a random non-zero UUID and is not derived from hardware or the
identity key. The displayed identity fingerprint is uppercase hexadecimal
SHA-256 of the exact SPKI DER bytes. A trust record binds:

```text
peer DeviceId, display name, SPKI bytes, fingerprint, verification time,
explicit capability grant
```

A known DeviceId with different SPKI bytes is an identity-change failure and
must never be overwritten silently.

## Pairing transcript v1

The transcript is a length-delimited binary structure. All integer lengths are
unsigned 32-bit big-endian; strings are UTF-8 without a terminator.

```text
bytes "FLOWSPAN-PAIR-V1"
u32(protocol major), u32(protocol minor)
party(initiator)
party(responder)

party :=
  u8(role: 1 initiator, 2 responder)
  bytes(device UUID in lowercase D text)
  bytes(display name, UTF-8, NFC-normalized)
  bytes(identity SPKI DER)
  bytes(32-byte random nonce)
```

Both parties sign `SHA256(transcript)` with their long-lived identity keys. Role
and ordering are fixed, preventing reflection and ambiguous sorting.

The six-digit short authentication string (SAS) is:

```text
uint32_big_endian(SHA256("FLOWSPAN-SAS-V1" || transcriptHash)[0..4]) mod 1_000_000
```

formatted as exactly six decimal digits. SAS is for interactive MITM detection,
not key derivation. Trust is created only after valid transcript signatures and
explicit signed acceptance from both parties. Rejection or timeout creates no
trust record.

## Signed pairing confirmation

Confirmation payload:

```text
bytes "FLOWSPAN-CONFIRM-V1"
bytes(transcript hash, exactly 32)
bytes(confirming DeviceId in lowercase D text)
u8(accepted: 0 or 1)
```

It is signed with the confirming identity using the same P1363 format. Both
confirmations must bind the same transcript hash and be `accepted = 1`.

After verifying both accepted confirmations, each endpoint signs a distinct
completion payload so an acceptance cannot be replayed as proof that the peer's
acceptance was verified:

```text
bytes "FLOWSPAN-PAIR-COMPLETE-V1"
bytes(transcript hash, exactly 32)
bytes(completing DeviceId in lowercase D text)
```

The initiator completion proof is sent and verified before the responder sends
its proof. Local Trust registration is not attempted until the endpoint has
verified the peer completion proof.

## Pairing wire envelope v1

Every pairing message is prefixed on direct TCP by a signed 32-bit big-endian
length limited to 1..4096 bytes. The message body begins with `FSP1` and one
kind byte:

```text
hello(kind = 1) :=
  u8(role)
  bytes(device UUID), bytes(display name), bytes(identity SPKI)
  bytes(32-byte nonce)
  u32(version count), repeated u32(major), u32(minor)

transcript-signature(kind = 2) :=
  bytes(signing DeviceId)
  bytes(transcript hash, exactly 32)
  bytes(P1363 identity signature, exactly 64)

confirmation(kind = 3) :=
  bytes(confirming DeviceId)
  u8(accepted: 0 or 1)
  bytes(transcript hash, exactly 32)
  bytes(P1363 confirmation signature, exactly 64)

completion-proof(kind = 4) :=
  bytes(completing DeviceId)
  bytes(transcript hash, exactly 32)
  bytes(P1363 completion signature, exactly 64)
```

Roles are `1 = initiator` and `2 = responder`. Versions are unique and strictly
increasing on wire. The ordered exchange is initiator hello, responder hello,
initiator transcript signature, responder transcript signature, followed by one
signed confirmation from each side. Confirmation sends and receives may overlap
so rejection can cancel a pending peer prompt. The initiator completion proof
then precedes the responder proof. The whole ceremony has a two-minute default
and ten-minute hard maximum. Trust registration occurs only after both
confirmations and both completion proofs verify; capability grants are chosen
and stored locally rather than carried as remote authority on this wire.
The one-shot pairing channel closes on every outcome and cannot be upgraded into
an operation channel. A successful pair reconnects through the separately
authenticated ephemeral-session handshake before carrying any Activity data.

One published listener may accept both protocol families. It classifies only
the first length-bounded hello envelope: `FSP1`, kind 1 selects pairing, while
`FSH1`, kind 1 selects the authenticated-session handshake. Any other magic,
kind, truncated envelope, oversized frame, or selection timeout closes that
connection. The pre-read frame is transferred to exactly one selected decoder
and is never interpreted twice. Pairing and authenticated sessions have separate
capacity limits within a hard maximum of 128 active inbound connections.

## Secure-session derivation v1

The authenticated handshake carries a fresh P-256 ECDH SPKI and 32-byte random
nonce for each role. Its transcript uses length-delimited, role-fixed encoding
and is signed by both paired identities. A reconnect is attempted only against
an existing trust record; the claimed DeviceId and identity fingerprint must
match that record before the connection can be upgraded.

Handshake transcript:

```text
bytes "FLOWSPAN-HANDSHAKE-V1"
u32(selected protocol major), u32(selected protocol minor)
hello(initiator)
hello(responder)

hello :=
  u8(role: 1 initiator, 2 responder)
  bytes(device UUID in lowercase D text)
  bytes(identity fingerprint in uppercase hexadecimal)
  u32(supported protocol version count, 1..16)
  repeated u32(protocol major), u32(protocol minor)
  bytes(ephemeral P-256 SPKI DER, at most 1024 bytes)
  bytes(random nonce, exactly 32 bytes)
```

Version lists are deduplicated and sorted before encoding. The selected version
is the highest exact version offered by both peers. Both the offered lists and
the selected version are covered by the transcript signature, so rewriting an
offer or forcing a lower selection invalidates authentication.

The unencrypted handshake wire message is limited to 4096 bytes and begins with
`FSH1`. A hello message is:

```text
4 bytes magic "FSH1"
u8(kind = 1), u8(role)
bytes(device UUID text), bytes(identity fingerprint)
u32(version count), repeated u32(major), u32(minor)
bytes(ephemeral SPKI), bytes(32-byte nonce)
```

An authentication message is:

```text
4 bytes magic "FSH1"
u8(kind = 2), u8(role)
bytes(transcript hash, exactly 32 bytes)
bytes(P1363 identity signature, exactly 64 bytes)
```

Each `bytes` field is prefixed with an unsigned 32-bit big-endian length. The
four messages on a fresh direct TCP connection are ordered:

1. initiator hello;
2. responder hello;
3. initiator authentication;
4. responder authentication, sent only after the responder validates the
   initiator's trust binding and signature.

On a listener shared by multiple peers, the responder parses the Device ID from
the bounded initiator hello only to select a current trust record. That claim is
unauthenticated and cannot authorize a session. An unknown ID receives no
Flowspan response; a known ID still requires the complete hello Device ID and
fingerprint match plus a transcript signature verified by the selected trusted
key. The listener reloads current trust and compares the authenticated key again
immediately before capability registration.

Every handshake wire message is itself prefixed by a signed 32-bit big-endian
TCP frame length bounded to 1..4096. Either parse, identity, version, signature,
or cancellation failure closes the candidate socket. After both sides validate
the peer authentication, protocol 1.0 and 1.1 upgrade the same socket to
encrypted control frames; their first control message provides only implicit
key-possession confirmation. Protocol 1.2 adds the explicit Finished exchange
defined below before upgrade.
The transport applies a 10-second default handshake timeout after TCP accept or
connect; callers may reduce it or increase it to at most two minutes.
The authenticated connection rejects every outbound or inbound control message
whose sender DeviceId or protocol version differs from the identities and
version bound by this transcript.

Given the raw ECDH secret and authenticated handshake transcript hash:

```text
salt = handshakeTranscriptHash
info = UTF8("FLOWSPAN-SESSION-V1")
okm  = HKDF-SHA256(rawSecret, salt, info, 80 bytes)

okm[0..32]   initiator -> responder AES-256 key
okm[32..64]  responder -> initiator AES-256 key
okm[64..80]  session identifier
```

The implementation must erase raw secret and derived key arrays when their
owner is disposed.

## Encrypted Finished exchange (protocol 1.2)

Protocol 1.2 adds explicit bidirectional key confirmation before a direct TCP
connection can be exposed as an authenticated control channel. Protocol 1.0 and
1.1 retain the original four-message compatibility path; when both peers offer
1.2, the signed highest-common-version transcript requires this exchange and a
network attacker cannot remove 1.2 from either offer without invalidating an
identity signature.

After the four signed handshake messages and session-key derivation, the
initiator sends one encrypted Finished frame and waits for the responder's
encrypted Finished. The responder verifies the initiator frame before sending
its own. The outer frame is the existing `FSE1` epoch-1 AEAD frame at sequence
zero in that direction. Its plaintext is:

```text
4 bytes magic "FSH1"
u8(kind = 3)
u8(role: 1 initiator, 2 responder)
bytes(handshake transcript hash, exactly 32 bytes)
bytes(session identifier, exactly 16 bytes)
```

The role, transcript hash, and session identifier must all match the local
authenticated handshake state using fixed-time comparisons for byte fields.
Malformed plaintext, wrong role, wrong transcript/session binding, AEAD failure,
missing Finished, or timeout closes the connection before upgrade. Successful
Finished consumes secure-frame sequence zero, so the first control message uses
sequence one. Finished proves possession of the derived directional traffic key;
it does not create new authority beyond the signed identity transcript and
current Trust Record.

The production desktop advertises 1.2 before 1.1 and 1.0. A peer that negotiates
1.0 or 1.1 remains interoperable but is explicitly a legacy session without the
new Finished evidence. Removing that compatibility path is a release-policy
decision separate from this wire addition.

## Encrypted frame v1

Control plaintext is limited to 256 KiB before encryption. Each direction owns
an independent key and strictly increasing sequence starting at zero.

```text
4 bytes magic "FSE1"
u32 key epoch, big-endian (starts at 1)
u64 sequence, big-endian
u32 ciphertext length, big-endian
ciphertext bytes
16-byte GCM tag
```

Nonce is `u32(epoch) || u64(sequence)`. Associated data is:

```text
bytes "FLOWSPAN-AEAD-V1"
bytes(16-byte session identifier)
u8(direction: 1 initiator->responder, 2 responder->initiator)
u32(epoch)
u64(sequence)
u32(ciphertext length)
```

The receiver accepts exactly its next expected sequence. Replay, gap, wrong
direction/session/epoch, malformed length, or tag failure is rejected without
advancing the counter. A key epoch must rotate well before sequence exhaustion;
the initial implementation rejects exhaustion and does not yet implement live
rotation.

## Live traffic-key update (protocol 1.3)

Protocol 1.3 retains the protocol-1.2 Finished exchange and adds live,
direction-independent traffic-key evolution toward one connection target epoch.
It follows the public TLS 1.3 KeyUpdate pattern at the concept level, without
copying an implementation: a sender authenticates one update record with its
current traffic key, then erases that key and begins the target epoch at sequence
zero. No new identity, Trust, or Capability authority is created. This first
version does not claim post-compromise security because it evolves the existing
traffic key rather than performing another ECDH exchange.

The update plaintext is exactly 10 bytes:

```text
4 bytes magic "FSR1"
u8(kind = 1, traffic-key update)
u8(flags: bit 0 requests a peer-direction update; all other bits zero)
u32 next epoch, big-endian (current epoch + 1, minimum 2)
```

For `next epoch = 2` with the peer-update flag set, the complete bytes are
`46535231010100000002`; their SHA-256 is
`919E1A6CECA322B61A0F98612E55C0584189AE166CC6685E8FB775FBDAD71F45`.
The plaintext is carried inside the sender's final `FSE1` frame under the current
key and epoch. Only after that complete length-prefixed frame is flushed does the
sender derive and install the next key. The receiver decrypts and strictly
decodes the record under its current receive state, requires exactly
`current epoch + 1`, then installs the matching receive key before accepting
another frame. Update records are transport control and are never exposed as
Activity `ControlMessage` values.

For each direction:

```text
salt = 16-byte session identifier
info = UTF8("FLOWSPAN-REKEY-V1") || u8(direction) || u32(next epoch)
next = HKDF-SHA256(current traffic key, salt, info, 32 bytes)
```

With a current key of byte `11` repeated 32 times, session identifier byte `22`
repeated 16 times, initiator-to-responder direction `1`, and next epoch `2`, the
next key is
`E1CEE8A87F7D1A22645CE8968C7226F68E7A790AF3C2D07DE8C0D80B80902591`.
Derivation occurs while the old key remains usable; successful installation
zeroes the old key before publishing the new epoch. Derivation or installation
failure faults and destroys the whole session.

The peer-update flag asks the receiver to advance its own send direction to the
named target epoch. If its send epoch is lower by exactly one, it sends one
update with the flag clear. If its send epoch is already equal or higher because
both peers requested simultaneously, it sends nothing. Therefore simultaneous
requests converge without a second rotation or response ping-pong. A target
epoch gap, overflow, replayed old epoch, skipped frame sequence, malformed
record, authentication failure, or update write/flush failure faults the
connection and erases its live traffic keys.

For protocol 1.3, a sender updates before application traffic would exceed
either 1,048,576 protected frames or 1 GiB of protected plaintext in the current
epoch. The already-authenticated Finished frame counts toward epoch 1. One
bounded update record is reserved as transition overhead at the policy boundary.
Protocol 1.0 through 1.2 never receive this record; 1.2 remains a Finished-capable
compatibility path but closes and reconnects at the usage bound. Protocol 1.0
and 1.1 retain their stronger legacy warning.

## Verification requirements

- RFC 5869 HKDF-SHA-256 test case 1.
- Generated identity sign/verify plus altered transcript/signature negatives.
- Two independent ECDH instances derive identical session material.
- Pairing transcript determinism, role binding, SAS equality, dual-confirmation,
  rejection, identity-key substitution, canonical wire golden fixture, seeded
  hostile decoding, completion-proof tamper, deadline, and direct TCP loopback
  tests.
- Initial-family golden/hostile selection plus same-port pair/close/authenticated
  reconnect, pairing/session capacity, selection-deadline, cancellation, and
  fatal-accept drain tests.
- Authenticated-handshake transcript/wire round trip, highest-common-version,
  claimed-ID/key substitution, altered-version signature, direct TCP loopback,
  and two-current-peer shared-listener tests.
- Protocol-1.2 Finished codec golden/hash fixture, wrong role/transcript/session,
  AEAD tamper, omission/timeout, first-control sequence, legacy 1.1 compatibility,
  and direct TCP loopback tests.
- Protocol-1.3 rekey codec and key-schedule golden vectors; single-initiator,
  simultaneous, automatic frame/byte-threshold, and repeated epoch updates;
  old-key erasure, old-epoch replay, epoch/sequence gap, malformed flags,
  write/flush/disconnect, cancellation, and authenticated loopback tests.
- AEAD round trip, independent directional keys, tamper, replay, sequence gap,
  wrong session/direction, malformed length, and maximum-size tests.
- Windows/macOS/Linux CI execution, followed by real-machine credential-store
  and provider evidence.

## Known gaps and release blockers

- The protocol-1.3 key-evolution format remains provisional until task 4.3b is
  implemented, independently reviewed, and supported by exact-commit evidence.
- No independent security review has approved these formats.
- Desktop pairing UI and physical two-device SAS evidence are not yet
  implemented. Protocol-1.2 Finished remains provisional until its task evidence
  and independent security review close.
- The platform credential-store adapters remain provisional and do not yet have
  the complete real-machine Windows/macOS/Linux acceptance evidence.
- In-memory identities are test/simulator infrastructure only, and the listener
  loopback tests are not physical-device or remotely reachable LAN evidence.

All gaps above remain v1 security release blockers.
