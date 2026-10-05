# Hosted regression reliability design

Preserve the original CI failure and separate the two causes. Use the actual
participant preparation/Stop path and actual authenticated loopback connections;
do not replace production lifecycles with a fake success adapter.

The participant fixture currently awaits preparation before releasing renderer
disposal, although preparation may itself own that disposal. Cover both legal
cleanup winners with test-owned barriers, retaining the original Fact identity
and adding a separate winner case. Every barrier is released in finally and
every started task is joined before fixture synchronization is disposed.

The inbound two-peer test currently puts a two-second wall-clock budget on a
success scenario. A controlled scheduling harness reproduces server timeout and
client EOF, but does not uniquely diagnose the original hosted Windows event.
Inject TimeProvider at the existing inbound authentication deadline boundary.
Existing public overloads and callers retain System time; production defaults
remain ten seconds, maximum two minutes, and the fixture's configured budget
remains two seconds. Keep caller cancellation linked to a separately owned
deadline token and dispose both owners. Test exact equality using a silent real
peer, then verify admission recovers for a trusted peer. No protocol, capability
or Trust change is intended.

Save actual RED/GREEN and diagnostic campaigns separately. Freeze final source
and complete runtime inventories for solution D/R, preserve all 2948 baseline
qualified identities, then use the new exact branch SHA for CI/CodeQL. Hosted
and local evidence remain separately labelled.
