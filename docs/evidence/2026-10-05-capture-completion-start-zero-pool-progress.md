# Same-Capture Start zero-pool checkpoint — 2026-10-05

Status: one finite MCC task4 acquisition outcome is accepted locally. Task4,
complete MCC/MSC, native and hosted gates remain open. Recorded base:
`1ab251135f3617bc18e39d5f67e38885b53e85a7` with a working-tree overlay.
Evidence: `/tmp/flowspan-capture-completion-stop-allocation-20261005/`.

`StartZeroPoolPushRejectsNativeBodyAndRetainsCaptureAfterKnownCleanup` has
healthy construction and Start completion acquisition, then actual ordinal2
pool push returns token0. No fatal is injected. Real Stop, both Dispose calls,
raw runtime teardown and outer GC precede the behavior/weak-graph assertions.

Compiled `start-zero-pool-red01` actually fails line23. Expected Start/Stop
results, selectors, native attempts, Start/Stop invocations and callbacks are
`(false,false,0,0,0,0,0,0)`; actual is `(true,true,1,1,2,1,1,2)`.
The zero token still permits native Start and subsequent Stop success; this
is behavioral RED after completed teardown, not a setup failure.

Minimal GREEN changes only the API with the shared ordinary zero guard before
selector/native body. Start reports InvalidOperationException; Start/Stop
selectors, native invocation attempts and callback effects remain0. Stop
returns false without an exception. The local Start facts are issued=false,
invocation-returned=false, result-settled=true/result=false and exit-settled=true.
Those last two are local unissued settlement, not evidence of a callback or
completed notification. Both Dispose failures are ordinary InvalidOperationException;
repeated Dispose preserves its first failure instance.

The already acquired Start caller releases once; its primitive normally retires
with no FirstFailure and zero live blocks/roots. Output removal, queue barrier,
stream/output/configuration/queue/source cleanup each execute once. Constructor
and removal pools pop once on their creating threads; zero Start pool gets no
guessed pop. Valid token/thread pairs match despite push/pop ordinal divergence.
Physical cleanup is confirmed, but unknown Start pool debt still retains the
complete shell/completion/primitive/operations/source/callback-marker graph and
`before+1` charge after raw teardown and GC. No retry or shell/count return follows.

RED01→GREEN01 uses identical complete test bytes. Single Debug/Release each1
and focused Debug/Release each129 pass, preserving the exact128 accepted
qualified identities plus this1 Fact with no removals. All five locked restore/
build stages exit0 with0 warnings/errors; GREEN test exits0 with no skipped or
nonterminal results.503 selected inputs, complete138-file runtime inventories,
raw commands and qualified TRX/DLL bindings are saved; receipts verify.
No new project/quality/full-solution/native or hosted gate was run. The last
actually executed project Debug/Release remain390.

- Test SHA256: `084cb38332caa382a1289de78b0bf25851cd63b98c4c7943e8b3f64c4f4e93a7`.
- GREEN API.

  SHA-256: `563a3f591ce2b62121ef4bf44939c71fe7222a198c27245e0299bfd66c51f117`.

- Auditor: `39a1f6b30cb833c9bf5715357b0a777ab97d210bbd950c3b3110c56e209b33d7`.
- Replay: `7a171f600730d6d0e39a85a1261fccb6a10eba2a0ec2c1c2f702bdb3c50dc925`.
- Saved report: `ad54aff30168cc46aefd3ee5f4e3efd7f2fa2f8d059143c4069678d423e01a58`.
- Selected-current report: `965ecedb5e945c20644d23642ae52529d3725f69c9fa6cf48dcc37f1e3547bcb`.

Standards:0 concrete findings. Spec:0 concrete findings. Root accepts this
finite behavior after actually executing `start-zero-pool-root-saved01` and
`start-zero-pool-root-current01`: both raw exits0, empty violations and reports
byte-identical to their worker counterparts. Both replay receipts verify.
Replay executes no tests. Selected-current equality covers503 inputs at the
recorded freeze, not the whole tree, and becomes historical after later edits.

```sh
bash /tmp/flowspan-capture-completion-stop-allocation-20261005/start-zero-pool-replay.sh start-zero-pool-next-saved01 saved
```

Use a fresh label. Shared code changes do not accept Stop-zero behavior here.
Unknown push/removal outcomes, observers/resource-use joins, races and outer-
drain fallback remain open. This is portable controlled-effect evidence, not
native-fault containment, global Capture admission, production sharing or
complete task4/MCC/v1 acceptance.
