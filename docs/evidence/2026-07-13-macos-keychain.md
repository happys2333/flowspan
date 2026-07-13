# Evidence: macOS Keychain identity and trust storage, 2026-07-13

Classification: **Local native API smoke** and **simulated/contract**

Branch: `codex/v1-foundation`

Source state: this document's repository revision

## Environment and command

The environment is the macOS arm64 host recorded in
[`2026-07-13-macos-foundation.md`](2026-07-13-macos-foundation.md).

```sh
dotnet test tests/Flowspan.Platform.MacOS.Tests/Flowspan.Platform.MacOS.Tests.csproj \
  --configuration Release --no-restore
```

Observed result: 10 passed, 0 failed, 0 skipped.

## What this proves

- The byte-oriented Security.framework/CoreFoundation interop resolves and runs
  on this macOS host without a CLI or plaintext-file fallback.
- A disposable `WhenUnlockedThisDeviceOnly` Generic Password item can create,
  reload the same DeviceId/fingerprint, and delete successfully.
- Concurrent first launches using a disposable service/account converge through
  atomic `SecItemAdd` duplicate detection.
- A separate disposable trust item persists a canonical trust snapshot across
  create/restart, atomically replaces capability updates with `SecItemUpdate`,
  persists revocation, and is absent after cleanup.
- Fake-boundary contracts prove update-before-add, no delete/add replacement
  window, retry-after-concurrent-create, payload limits, and pre-cancelled
  mutation behavior.
- Fake-boundary contracts verify canonical payload round trip, temporary buffer
  clearing, malformed payload rejection without replacement, and pre-cancelled
  calls that do not reach Keychain.
- Every native test uses a unique account and deletes it in `finally` cleanup.

## What this does not prove

- ACL/prompt behavior for a signed and notarized Flowspan application bundle;
- behavior while the login Keychain is locked, during logout, across multiple
  macOS users, or after OS upgrade/migration;
- behavior at the full 64-peer/64 KiB trust bound;
- packaging entitlements, accessibility/capture permissions, or any other
  platform capability;
- Windows or Linux credential storage.

Those remain release evidence gates. Hosted macOS CI run
[29233261468](https://github.com/happys2333/flowspan/actions/runs/29233261468)
independently passed the committed native and contract tests for `c3a5aa2`; this
run predates the trust adapter. The trust adapter still needs its own hosted
macOS run and does not substitute for signed-package or locked-profile evidence.
