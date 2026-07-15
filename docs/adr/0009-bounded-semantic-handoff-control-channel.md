# ADR 0009: Carry bounded Semantic Handoffs on the authenticated control channel

- Status: Accepted for desktop task 7.3a
- Date: 2026-07-15
- Decision owners: Flowspan maintainers

## Context

The foundation simulator proves semantic Activity resume, and the desktop already
owns one authenticated, encrypted, Device-ID-elected control channel per trusted
peer. The first user-visible operation needs an honest vertical slice without
claiming arbitrary process migration, Remote Window media, Move, Mirror, or
driver transfer.

The channel was originally composed with `activity.offer` as an idle admission
placeholder. Capability grants are peer-relative: local `activity.offer` allows
the peer to send an Activity here, while local `activity.receive` allows this
device to disclose an Activity to that peer. Requiring only `activity.offer` for
the shared bidirectional channel can deadlock a valid one-way Handoff depending
on which Device ID owns the connector. Requiring both grants would grant and
retain more authority than the operation needs.

## Decision

The first desktop Activity kind is the existing bounded `workspace.note/v1`
adapter. A Handoff resumes a semantic copy on the target and always preserves the
source. The preview and receipt surface must say so, keep the global state
`NOT SHARING`, and name Remote Window as unavailable in this build.

The reusable Activity control profile is admitted when the local Trust Record
contains `activity.offer` **or** `activity.receive`. The coordinator records
whether a session requirement is all-of or any-of; existing callers remain
all-of by default. Removing one any-of alternative keeps the session, while
removing the final alternative drains it. Connector ownership remains the
canonical ordinal Device ID rule from ADR 0008.

Channel admission is not operation authorization:

- the source checks its local grant of `activity.receive` for the target before
  exposing a descriptor payload;
- the target binds the sender to the authenticated Device ID and current Trust,
  then checks local `activity.offer` before adapter use.

The version-1 transfer body has an exact field set and carries the Operation ID,
kind, deadline, target placement, request digest, and the bounded descriptor with
payload and descriptor digests. Its deadline must fit the authenticated envelope
lifetime. The payload is protected by the existing authenticated session's
directional AES-256-GCM framing.

The response is a payload-free Operation Receipt. The sender accepts it only when
the authenticated participants, negotiated protocol, correlation ID, Operation
ID/kind, Activity ID/kind, and descriptor digest match the pending transfer.
Unknown message types, malformed fields, digest mismatch, unsolicited or
mismatched receipts, and duplicate peer sessions fault closed. A disconnect
after send but before a verified receipt becomes `AcknowledgementLost`; it never
causes source cleanup.

Production lifecycle is identity -> Trust -> Activity runtime -> optional local
network. Shutdown reverses the authority boundary: network sessions -> Activity
runtime -> Trust -> identity. Failed Activity initialization may be retried
without reopening already-ready identity or Trust dependencies.

## Alternatives considered

### Add a second Activity TCP connection

This duplicates discovery, authentication, revocation, reconnect, status, and
Device ID ownership logic. It also creates another channel whose relationship to
the visible trusted-peer status would need reconciliation.

### Require both Activity grants for the shared channel

This avoids direction-specific admission logic but blocks least-privilege
one-way relationships and makes connector ownership affect whether a legitimate
Handoff can start.

### Admit every trusted peer and authorize only messages

This retains unnecessary authenticated sessions for zero-Capability peers and
weakens the existing coordinator's ability to drain sessions when authority is
removed.

### Implement generic application or process migration first

Flowspan cannot truthfully serialize arbitrary process memory, unsaved internal
state, credentials, or unsupported application state. A portable note is a real,
testable semantic Adapter rather than a migration promise.

## Consequences

- One encrypted control channel can support a least-privilege one-way Handoff in
  either Device ID ordering.
- Capability names and UI copy must retain their peer-relative direction.
- Receipts are useful for result and correlation diagnostics without retaining
  note content.
- A one-shot Handoff is not a live sharing session and offers no misleading undo;
  each device owns its resulting copy.
- Move, replace, swap, Mirror, driver transfer, Remote Window media, persistent
  Activity storage, physical-LAN proof, and native accessibility evidence remain
  separate work.

## Evidence and limits

Deterministic tests cover strict codecs, authorization direction, any-of
admission/downgrade, idempotency, faulted receipts, acknowledgement loss, UI
preview/receipt state, keyboard automation metadata, and lifecycle ordering.
Same-host TCP tests run the production encrypted framing and both one-way Device
ID ownership arrangements. Hosted Windows/macOS/Linux runs can prove these
portable contracts on the final commit; they cannot prove physical multicast,
cross-machine behavior, native prompts, or packaged accessibility.

## Revisit triggers

Revisit the message family when more Activity kinds require negotiated Adapter
capabilities, when concurrent operation flow control needs a dedicated channel,
when a relay transport is introduced, or when durable operation recovery replaces
the current in-memory desktop journal.
