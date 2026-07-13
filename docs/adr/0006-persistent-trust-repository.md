# ADR 0006: Bounded platform-protected trust repository

- Status: Accepted; core and all adapters implemented, Linux desktop evidence pending
- Date: 2026-07-13

## Context

Trust records bind a peer DeviceId to a verified P-256 public identity and an
explicit capability grant. They must survive restart, and a revoked identity
must not silently return after an interrupted update. R9.6 requires platform
credential-store protection where available. The current `InMemoryTrustStore`
is intentionally degraded and cannot satisfy that requirement.

The simulator design mentioned a versioned JSON document, but directly writing
trust as ordinary plaintext JSON would expose the device graph and capability
policy, permit undetected edits, and create a second mutation path around active
session shutdown. Private identity keys remain in the separate identity store
selected by ADR 0005 and are never included in this repository.

## Options considered

| Option | Advantages | Rejected cost or risk |
| --- | --- | --- |
| Plain JSON with atomic rename | inspectable; minimal code | no confidentiality or integrity; violates the approved security baseline |
| SQLite or another embedded database now | queries and mature transactions | adds package/native packaging surface before the bounded v1 access pattern needs it; encryption and key management still remain |
| One platform-protected, versioned binary snapshot | small core, deterministic tests, no new dependency, reuses ADR 0005 platform mechanisms | whole-snapshot rewrite; requires an explicit peer/size bound and platform availability evidence |
| Encrypted file plus a separate credential-store data key | scales beyond a single secret item | cross-store crash consistency, rollback anchoring, rotation, and recovery add substantial state before v1 needs the capacity |

## Decision

- `Flowspan.Security` owns an `ITrustStore` authority with synchronous snapshot
  reads and serialized asynchronous mutations. Production session admission,
  grant updates, and revocation use `TrustSessionCoordinator`; callers do not
  mutate a second repository instance.
- `ITrustPayloadStore` is the narrow persistence port. `LoadAsync` returns an
  opaque byte snapshot and `SaveAsync` atomically replaces the complete snapshot
  or leaves the previous snapshot readable. It reports
  `OperatingSystemProtected` or `DegradedTestOnly`; there is no implicit
  plaintext implementation.
- `TrustStorePayloadCodec` uses a deterministic binary `FSTR` format with an
  explicit version, canonical DeviceId order, exact UTC verification ticks,
  canonical UTF-8 display names, P-256 SPKI bytes, and a checked capability bit
  mask. Unknown versions/bits, duplicates, malformed lengths, non-canonical
  order, invalid keys, trailing bytes, and oversized input fail closed.
- The v1 repository holds at most 64 trusted peers and at most 64 KiB encoded.
  Exceeding either bound is a visible resource-limit failure, never truncation or
  eviction. This is a safety bound, not a claim that every native backend has
  yet been verified at the maximum size.
- A mutation builds and validates a complete candidate snapshot, asks the
  payload store to replace it, and publishes the new in-memory snapshot only
  after persistence succeeds. A failed or cancelled save leaves both the old
  in-memory authority and the previously committed payload in force.
- Startup decodes the protected snapshot before exposing the store. Missing data
  produces an empty store; corrupt, unsupported, or unavailable data blocks the
  store from opening and is never replaced automatically.
- Windows protects the bounded blob with CurrentUser DPAPI using a trust-specific
  entropy/context distinct from device identity. Under a persistent per-profile
  coordination lock it writes and fsyncs a same-directory temporary file, then
  atomically replaces the prior snapshot; cancellation or write failure leaves
  the prior file in force. macOS uses a dedicated
  `WhenUnlockedThisDeviceOnly` Keychain item: `SecItemUpdate` atomically replaces
  an existing snapshot; initial update/add and a retrying update resolve a
  concurrent creator without a delete window. Linux uses a separately attributed
  Secret Service item through the `secret-tool` boundary. Its Base64 stdout cap
  is selected per invocation so identity remains at 4 KiB while trust permits
  only its calculated 64 KiB payload envelope; stderr remains at 4 KiB. Store
  replacement runs under a persistent per-user coordination lock, and secret
  bytes appear only on stdin. Adapter-specific limits or failures are reported
  structurally. Linux real-desktop and remaining matching-runner evidence are
  separate implementation gates.

## Security boundary

Platform credential protection defends data at rest from ordinary file reads and
accidental edits. It does not defend a fully compromised user account or kernel;
such an attacker can invoke the same per-user credential APIs. Windows protected
file rollback by an actor already controlling the user profile is also outside
the v1 local-compromise boundary and must not be described as rollback-proof.
Normal crashes and failed writes are in scope and must preserve the last
committed snapshot.

## Consequences and verification gates

- Codec golden fixtures and hostile-input tests freeze the local format before a
  native adapter persists it.
- Repository tests cover empty startup, restart, identity-change refusal,
  capability update, revocation, concurrent mutation, corrupt data, save
  failure, cancellation, and no publication before durable replacement.
- Coordinator tests run against the persistent implementation so a durable
  revoke precedes session shutdown and new admission remains blocked.
- Each platform adapter needs create/reload/update/revoke/corrupt/cancel tests on
  its matching hosted runner plus separately classified real-profile evidence.
- The repository can later migrate behind `ITrustStore`; migration must be
  versioned, fail closed, and must not expose a bypass around the coordinator.
