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
the peer authentication, the same socket is upgraded to encrypted control
frames. The first encrypted control message also provides key possession
confirmation; there is not yet a separate encrypted Finished message.
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

## Verification requirements

- RFC 5869 HKDF-SHA-256 test case 1.
- Generated identity sign/verify plus altered transcript/signature negatives.
- Two independent ECDH instances derive identical session material.
- Pairing transcript determinism, role binding, SAS equality, dual-confirmation,
  rejection, identity-key substitution, canonical wire golden fixture, seeded
  hostile decoding, completion-proof tamper, deadline, and direct TCP loopback
  tests.
- Authenticated-handshake transcript/wire round trip, highest-common-version,
  claimed-ID/key substitution, altered-version signature, direct TCP loopback,
  and two-current-peer shared-listener tests.
- AEAD round trip, independent directional keys, tamper, replay, sequence gap,
  wrong session/direction, malformed length, and maximum-size tests.
- Windows/macOS/Linux CI execution, followed by real-machine credential-store
  and provider evidence.

## Known gaps and release blockers

- No key rotation/rekey protocol exists.
- No independent security review has approved these formats.
- Desktop pairing UI, production-listener protocol multiplexing, physical
  two-device SAS evidence, and an explicit encrypted Finished exchange are not
  yet implemented.
- The platform credential-store adapters remain provisional and do not yet have
  the complete real-machine Windows/macOS/Linux acceptance evidence.
- In-memory identities are test/simulator infrastructure only, and the listener
  loopback tests are not physical-device or remotely reachable LAN evidence.

All gaps above remain v1 security release blockers.
