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

## Secure-session derivation v1

The authenticated handshake carries a fresh P-256 ECDH SPKI and 32-byte random
nonce for each role. Its transcript reuses length-delimited, role-fixed encoding
and is signed by both paired identities. The exact handshake message exchange is
still pending implementation; no session may be production-enabled before it is
added to this ADR and downgrade tests exist.

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
  rejection, and identity-key substitution tests.
- AEAD round trip, independent directional keys, tamper, replay, sequence gap,
  wrong session/direction, malformed length, and maximum-size tests.
- Windows/macOS/Linux CI execution, followed by real-machine credential-store
  and provider evidence.

## Known gaps and release blockers

- The authenticated ephemeral handshake exchange is not yet implemented.
- No platform credential-store adapter exists.
- No key rotation/rekey protocol exists.
- No independent security review has approved these formats.
- In-memory identities are test/simulator infrastructure only.

All gaps above remain v1 security release blockers.
