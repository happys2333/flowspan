# ADR 0005: Platform-protected device identity storage

- Status: Accepted architecture; native adapters in progress
- Date: 2026-07-13

## Context

Flowspan's long-lived P-256 identity key and trust bindings must survive restart
without being written as plaintext to ordinary application storage. The core
needs one narrow lifecycle contract while each desktop OS uses its normal
per-user protection mechanism. An unavailable platform store must be reported;
it must not silently fall back to a plaintext file.

The stored identity payload is small: format version, DeviceId, normalized
display name, and PKCS#8 private key. Platform adapters treat the complete
payload as opaque secret bytes, clear temporary buffers, and atomically reject a
second create so concurrent first launches converge on one identity.

## Evidence gathered

NuGet metadata and upstream repositories were queried on 2026-07-13:

| Candidate | Version | License | Maintenance signal | Relevant constraint |
| --- | --- | --- | --- | --- |
| `System.Security.Cryptography.ProtectedData` | 10.0.9 | MIT | Microsoft verified/serviceable package; `dotnet/runtime` active | byte-oriented Windows DPAPI only |
| `Meziantou.Framework.Win32.CredentialManager` | 3.0.1 | MIT | verified package; upstream pushed 2026-07-13 | active Windows wrapper, but public secret API is `string` |
| `GnomeStack.Os.Secrets` | 0.1.3 | MIT | 4,516 downloads; upstream code last pushed 2023-12-06 | byte API and all three OSes, but low adoption/stale release; Linux requires libsecret/gnome-keyring |
| `Tmds.DBus.Protocol` | 0.94.2 | MIT | 8.4M downloads; upstream pushed 2026-07-11 | maintained low-level D-Bus building block, not a Secret Service client |
| `iTrooz.FreeDesktopSecrets` | 1.0.1 | LGPL-2.1-only OR LGPL-3.0-or-later | 11,811 downloads; upstream pushed 2025-02-17 | ready protocol client but license and extra logging/D-Bus dependencies broaden review |

Additional searches found no mature, actively maintained, verified package that
provides a byte-oriented Keychain, Credential Manager/DPAPI, and Secret Service
API with a smaller risk surface. `Devlooped.CredentialManager` bundles a broader
Git Credential Manager implementation and uses a package EULA; it is not a
minimal fit for an identity key.

Sources:

- NuGet v3 search, registration, flat-container metadata, and nuspec files;
- <https://github.com/dotnet/runtime>;
- <https://github.com/meziantou/Meziantou.Framework>;
- <https://github.com/gnomestack/dotnet>;
- <https://github.com/tmds/Tmds.DBus>;
- <https://github.com/iTrooz/FreeDesktopSecrets-CS>.

## Decision

- `Flowspan.Security` owns `IDeviceIdentityStore`, atomic provisioning, and the
  explicit `OperatingSystemProtected` versus `DegradedTestOnly` classification.
- `DeviceIdentityPayloadCodec` owns one bounded binary format with `FSID` magic,
  version 1, an RFC 4122 big-endian DeviceId, a canonical UTF-8 display name,
  and a PKCS#8 P-256 private key. Payloads are limited to 1 KiB and reject
  unknown versions, malformed lengths, non-canonical names, invalid keys, and
  trailing bytes.
- The in-memory implementation is test-only, clears its PKCS#8 buffer, and is
  always classified `DegradedTestOnly`.
- Windows uses `System.Security.Cryptography.ProtectedData` 10.0.9 with
  `DataProtectionScope.CurrentUser`, fixed Flowspan entropy/context, and an
  atomically created per-profile blob. No plaintext private key is written.
  Concurrent first launches write unique same-directory temporary files and use
  a no-overwrite rename so exactly one protected identity wins.
- macOS uses a narrow byte-oriented Security.framework Keychain adapter. It
  stores one generic-password item scoped by a stable Flowspan service/account;
  the item is `WhenUnlockedThisDeviceOnly`, and the adapter is isolated behind a
  native boundary so contract tests do not require Keychain access. Native calls
  build bounded CoreFoundation dictionaries with owned SafeHandles and return
  structured status/recovery information without converting secrets to strings.
- Linux invokes the standard `secret-tool` client for the freedesktop Secret
  Service, passes secret bytes through standard input rather than arguments, and
  captures output as clearable byte buffers. Missing `secret-tool`, missing
  session bus, locked collection, or denied prompt is an explicit unavailable or
  permission result. A direct `Tmds.DBus.Protocol` adapter remains a replacement
  option if packaging `secret-tool` proves unreliable.
- Trust/history persistence will use a separate authenticated repository; it may
  wrap its data-encryption key with this platform store. It will not serialize
  private identity keys into the general repository.

## Consequences and verification gates

- Adding the Microsoft Windows package requires a locked dependency update,
  license inventory, vulnerability scan, and CodeQL/CI run.
- Windows contract tests use a fake byte protector with the real atomic file
  boundary on every OS. A platform-conditional test exercises actual CurrentUser
  DPAPI only on Windows and verifies explicit rejection elsewhere.
- Contract tests must cover missing, create, reload, concurrent create, delete,
  corrupt payload, cancellation, unavailable backend, and no-silent-downgrade
  behavior.
- Codec tests must preserve identity and fingerprint on round trip and reject
  hostile payload shapes before any native adapter consumes the result.
- A native smoke test must create, reload, and delete a disposable Flowspan test
  identity on a real user profile for each OS. Hosted runner success is useful
  compilation/contract evidence but does not replace this gate.
- macOS prompts/ACL behavior, Windows profile/service contexts, and Linux desktop
  collection unlock behavior remain release evidence items.

No adapter may report `OperatingSystemProtected` until it is actually backed by
the selected OS mechanism.
