# macOS delegate ownership prerequisite design

Use one internal, portable `MacOSRemoteWindowCallbackOwner` module with an
explicit bounded pool, fixed reservation, immutable self-address registration,
one-time stream binding, local admission, terminal-once delivery and managed
invocation retirement. Native pointers are compared only as opaque identities;
the reducer never dereferences them. Lifecycle is Reserved → PublishedUnbound
→ BoundOnce → Active → Retired; terminal retirement is monotonic.

Before invoking an external handler, capture its admitted owner under a tiny
state gate, close admission for terminal events, and leave that gate. Track
ThreadStatic callback ancestry and AsyncLocal descendant ancestry. Retirement
checks both before entering the gate, closes new admission, and publishes one
asynchronously continued managed-invocation completion. A closed published
mapping is a tombstone, not an available slot. This proves only managed entries
already admitted, not that native code can never call again.

The independent `tools/Flowspan.MacOS.DelegateProbe` references the platform
assembly for this internal candidate. It is excluded from the solution and
adds no NuGet dependencies. One process-rooted native-probe pool retains every
published NSObject bridge; no published address is freed/reused before process
exit. Document this deliberate finite retention separately from real cleanup.
Never wire the owner into current Capture or `CreateProduction()` in this slice.

Validate public SDK SCStreamDelegate method signatures with typed objc_msgSend
and nonthrowing reverse entries for terminal, active and inactive callbacks.
The synthetic probe exercises forced GC, GCD dispatch, terminal-before-binding,
retirement with an in-flight managed invocation and late tombstone callbacks.
Fake stream identities are never messaged or dereferenced. Any uncertain native
ABI or process failure is a failed probe, not a successful cleanup claim.

Portable tests execute the same owner behavior on all three OSes. Local matching
host Debug/Release synthetic execution is a separate evidence level. It proves
only those Objective-C/GCD calls actually executed; no ScreenCaptureKit session
or source-loss behavior is inherited from it.
