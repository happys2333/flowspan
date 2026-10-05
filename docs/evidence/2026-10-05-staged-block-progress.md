# Staged macOS Block ownership progress — 2026-10-05

Historical primitive checkpoint; later enumeration composition is recorded in
the [composition checkpoint](2026-10-05-enumeration-composition-progress.md).

This closes no enumeration task or v1 requirement by itself. Enumeration still
uses the legacy Create adapter; same-batch staged composition is next work.

The opt-in primitive now exposes Prepare → attach inert owner → AcquireCopy.
Prepare creates only managed state. Root allocation, native copy and owned
release have separate attempted/confirmed facts. Unknown effects hide the
borrowed pointer, retain diagnosis and never retry or ask a finalizer to guess
cleanup. Legacy Capture Create behavior remains separate and regression-tested.
Actual last physical capture disposal plus normal FreeRoot return publishes
NativeCaptureRetirement. ManagedInvocationDrain also requires all admitted
action/failure/completed handling to exit; it still precedes final native ABI
return and is not a production queue-drain promise.

## Portable primitive evidence

`/tmp/flowspan-staged-block-20261005/` preserves10 actual RED→GREEN pairs,
including root/copy after-effect uncertainty, consumed release/free faults,
managed exit, reentrant acquisition/disposal, invalid roots/copy association and
fatal observer priority. Complete test bytes and qualified identities are bound
within each pair. Compiler and recorder candidates remain distinct failures.
Final stages34/37 pass focused Debug/Release19 each;35/36 pass macOS project348
each, retaining all329 baseline identities. This worker snapshot is f2cbdb8 plus
only its three Block/operations/staged-test overlays, not the combined effects
implementation. Format and diff checks actually exit0.

Read-only saved-data replay (root also actually executed, exit0):

```sh
python3 /tmp/flowspan-staged-block-20261005/audit-progress.py
```

Script SHA256 `c2bff3a0e7bcd90ae4df0c4c2d30da3cc889b20cd6d944aee51f89483d4aa056`;
report SHA256 `db1899d51b7882fe3c21836745276d3d72e86bd4dd488b70a16e58f8f23695ec`,
unchanged after root replay, `violations=[]`.

Final source SHA256:

- Block: `308fabaef4e0c485ea00926c6f611d259f338709cfc9fdbf15ed12cd9617649b`.
- Operations: `304f05e608f42d778edb194d2f64438d202a454e2e9cca06a737cc9e3986815d`.
- Tests: `2b3a7d835a5f4932840c0d8dcc5a7732e2dbf87b7013bde6dc75ebba3dd08ef3`.

## Actual task-owned native ABI evidence

The new `tools/Flowspan.MacOS.BlockProbe/` executes real Prepare/AcquireCopy,
_Block_copy/_Block_release and two-argument unmanaged invocation. Debug and
Release each actually run in a fresh externally bounded process, exit0 with
empty stderr. The extra heap copy returns the same address without another
capture helper entry. Caller release cannot retire the extra copy. Later last
release confirms actual GCHandle.Free while a managed invocation is still active;
managed drain stays pending until it exits. A dedicated thread join additionally
observes actual ABI return in this controlled probe. Helper count never authorizes
retirement. No AppKit, SCK, TCC, window, pixel, input or network action is used.

Actual host is macOS27.0.1 build26A434 ordinary arm64, SDK10.0.301/runtime10.0.9;
this does not establish the minimum platform, Intel or arm64e.
`/tmp/flowspan-enumeration-native-block-20261005/` preserves a rejected source-freeze
candidate with no native execution, then successful Debug stage02 and Release03.
They share765 frozen source inputs,4198 SDK/runtime/reference/host inputs and190
generated artifact inputs per configuration with unchanged before/after bindings.
The two production Block files exactly match the hashes above, but its full source
is not the later combined enumeration snapshot.

Root actually replays the read-only audit, exit0 and `violations=[]`; the saved
report remains byte-identical:

```sh
python3 /tmp/flowspan-enumeration-native-block-20261005/audit.py
```

Script SHA256 `d1cb4c22ef1a01922656d1b7fa2ebf363059d8ad368af745630f82c73aaedace`;
report SHA256 `eef0418f170e293e74bd3a7c034b08cc77eb137dd3a988ff6a34626b4f172bd9`.
D/R native stdout SHA256:
`8dc7f5120c09cab4343a3a22d98f081aa7486c456a734f4029e35932045096dc`.

This is actual Block ABI proof, not SCK dispatch/copy retirement, native Capture,
the current full enumeration, cross-platform CI, production sharing or v1
acceptance. Same-batch composition, complete fault/lifetime contracts, fresh
exact-commit hosted gates and remaining product/release acceptance stay open.
