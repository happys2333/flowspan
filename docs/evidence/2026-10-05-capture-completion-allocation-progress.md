# Same-Capture allocation uncertainty — 2026-10-05

Status: the single Start allocation-after-effect case is accepted locally as
direct GREEN. No new production repair or behavioral RED is claimed. Stop,
invalid-return and caller-release cases and the complete MCC gates remain open.
This is the historical first-row checkpoint; the subsequent
[eight-row matrix](2026-10-05-capture-completion-uncertainty-matrix.md) now closes
the remaining finite task3 coverage, without closing complete MCC.
Base HEAD: `1ab251135f3617bc18e39d5f67e38885b53e85a7` with the accepted
[late root-free](2026-10-05-capture-completion-root-free-progress.md) overlay.
Evidence: `/tmp/flowspan-capture-completion-allocation-20261005/`.

The existing external runtime actually allocates a fixture GCHandle, then
throws Aggregate → IOException → original fatal before returning its pointer.
The same production Capture already owns its staged one-argument completion and
primitive. Root allocation is attempted but unconfirmed, no copy is attempted,
and no native invocation, guessed free or caller release occurs. Start, Stop and
both Dispose calls preserve the original fatal. Known stream/output/configuration/
queue/source cleanup occurs once. After raw fixture teardown removes its own
roots, outer GC still observes the complete original weak graph and charged
Capture; no production accounting reset or invented retirement is used.

Debug `tracer01` and Release `tracer-release01` each actually pass the same single
qualified Fact. Locked restore/build/test exit0,0 warnings/errors.503 selected
C# inputs match across configurations and differ from the accepted baseline only
by this Fact/helper insertion; existing ownership bodies and production/runtime
support remain byte-identical. This is not a new focused/project/solution gate.

- Test SHA256: `9431fe5d4bad5436e595d55176556869c1ff397c936c685dbeddcf6b7d0791f1`.
- API remains unchanged.

  SHA-256: `ef8abaa78f6dad313c07a31a993120682b931f3d17f77ce533b354d2a93ebf89`.

- Source manifest: `4d9b64205f9ffe43090e3b06c3dcd0811da7a49521cbf94ab1e4a87f4fa940e5`.
- Auditor: `39dc41934c384bf2499e39a6bdb88a5a196442c73c524216fcfd5b7457fe891e`.
- Replay: `50ad00a54a6fbb286a602a8823e36f7a2fef94227cad5516d465c010641c19ae`.
- Saved report: `d982219b4442882ac7e91ec8e2ea6fd29309c2fd070f40aefcbea3d4a5e84390`.
- Selected-current report: `78576e129686e028dbc93a23f023060f5f4379c7b1d3aacc470fa59d2f7d80ee`.

One direct read-only auditor found no finite-scenario defect. Actual root
saved/current replay and corresponding worker `cmp` each exit0 with no
violations. Replay checks full raw command/receipt/source/runtime bindings and
qualified TRX counters/storage/DLL bytes; it does not rerun a test:

```sh
bash /tmp/flowspan-capture-completion-allocation-20261005/replay.sh next-saved01 saved
```

Later source edits supersede selected-current equality. This controlled-effects
case is not actual native allocation-fault containment, native/hosted acceptance,
process-wide Capture admission or v1. All pool/resource-use/race/drain and later
MCC/MSC obligations remain required.
