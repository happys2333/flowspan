# macOS Capture completion ownership (MCC)

Status: accepted for staged implementation; acceptance unverified.
Read-only source checkpoint: `1ab251135f3617bc18e39d5f67e38885b53e85a7`.
MEP's exact `1ab2511` scoped hosted gate passes independently; it does not test
this subsequent implementation or close MCC acceptance.
Trace: NR8/NR10; MSC2/MSC6/MSC7 ownership prerequisites, not MSC9 completion.

As the native Remote Window candidate, I need Start/Stop completion factory
failure and retained native copies to remain owned by their original Capture,
without confusing a settled result or first callback exit with complete cleanup.

## Current boundary and scope

The actual `Capture` has one rooted shell (`callbackRoot` GCHandle), a diagnostic
`retainedOwners` count, an exact-output lookup and Start/Stop state. Each
`MacOSRemoteWindowCaptureBoundary` admits at most one operation for that boundary.
Neither fact is a process-wide Capture capacity limit. The max-16 router Capture
permit is implemented separately but is **not composed into this Capture**.
Source/catalog/batch pool charges are separate and do not become Capture permits.

This slice adds no second Capture state machine, global registry, completion pool
or unbounded quarantine list. Each existing shell owns at most one Start and one
Stop completion obligation. Unknown effects keep that original rooted shell and
its existing retained-owner accounting; this is per-shell bounded retention, not
proof of global bounded Capture admission. MSC task 3b.2b/3b.2c remains open.

## Acceptance criteria

- MCC1: Before a Start or Stop completion attempts a per-operation root or Block
  copy effect, the same production Capture shall hold its inert completion owner
  and actual primitive shell. Preparation shall make no per-operation root/copy
  effect. Capture shall attach the owner before acquisition, not after a factory
  that may have partially succeeded returns. The exact one-object-argument ABI
  shall be preserved; legacy zero/two-argument users shall not be migrated.
- MCC2: When root allocation, Block copy, caller-owned release or root free
  throws or returns an invalid ownership result, the owner shall retain separate
  attempted/confirmed facts and its complete original graph. Unknown acquisition
  shall receive no guessed release; unknown release/free shall receive no retry,
  including Dispose/finalizer/reentry. Known independent cleanup shall still be
  attempted once. A failure observer shall not replace an earlier nested fatal.
- MCC3: When native completion invokes, the same Capture shall distinguish first
  admitted result, result settlement, admitted managed resource-use exit,
  first-idle observation, caller-owned Block release, last physical native-copy
  retirement, terminal managed invocation drain and final ABI return. Duplicate,
  overlapping or late callbacks shall not repeat result/SourceUnavailable side
  effects, reopen local delivery, regress confirmed Stop or touch released native
  resources. Every primitive invocation remains independently tracked.
- MCC4: While Start/Stop handoff or acquisition is in flight, Capture shall
  serialize publication with owner release without holding a state gate across
  external effects. Only a confirmed acquired pointer may be borrowed for native
  invocation. Stop/Dispose racing acquisition shall retain the exact owner and
  shall not issue a guessed native call or duplicate release. Cached successful
  Start shall not bypass an already recorded local ownership failure.
- MCC5: When factory, invocation, callback or cleanup fails, independently known
  completion, pool, stream, output, queue, configuration and retained-source
  obligations shall each keep their own single-attempt cleanup facts and original
  fatal ordering. Native calls, observers and waits shall occur outside state
  gates. Confirmed autorelease pools shall be popped on their creating synchronous
  thread; unknown pool acquisition/pop shall remain unconfirmed, not transferred
  to arbitrary async cleanup. A zero or unconfirmed pool acquisition shall not
  authorize that scope's native body or a guessed pop. The four existing scopes
  (construction, Start, Stop, output removal) shall have fixed facts on the
  original shell. An unsettled
  construction pool scope shall prevent shell-root return. Pool pop and original
  primary/fatal selection shall precede exact failed-shell handoff and subsequent
  independent cleanup; pool unwind failure shall not lose that shell or hide its
  original failure behind the replaceable thread-local handoff slot.
- MCC6: While completion ownership or lifetime is uncertain, the original Capture
  shell, actual primitive, operations/source and callback graph shall remain
  reachable through its durable root, even after GC, boundary loss or failed
  factory-slot replacement. Only confirmed independent owner cleanup plus both
  completions' required native/managed lifetime facts may free that shell root
  and decrement its retained-owner accounting. No new Capture permit is returned
  by this slice because none is composed yet.
- MCC7: When cleanup is requested, closing late result/resource-use admission and
  joining admitted managed resource users in action, failure and completed
  notification regions shall precede dependent native release. This pre-release
  join shall not wait on `ManagedInvocationDrain`, which additionally requires
  post-release native root retirement.
  Confirmed Stop/output-removal/sample-barrier cleanup shall remain independently
  possible; it shall not be made to wait for a Block copy held by the stream being
  released. Caller +1 release must precede waiting for its own final retirement.
  Physical sample/output proof and complete completion/shell cleanup shall remain
  separate; the outer boundary's `IsDrained` fallback shall not mask completion
  debt or authorize shell/source-binding return. Direct/active-descendant self-join
  shall reject before waiting.
- MCC8: Before acceptance, the first same-Capture after-effect behavioral tracer
  shall actually fail before its ownership-order repair and pass afterward with
  unchanged test bytes. For that RED, a shared real one-argument adapter accepting
  native effects shall perform Prepare then AcquireCopy inside `CreateCompletion`;
  Capture shall retain its old assignment-after-return ordering. The tracer shall
  drive actual Stop/Dispose and inspect original-shell owner graph/accounting
  without test strong references. The GREEN shall only move attachment before
  acquisition. Subsequent finite factory/pool/lifetime/fault/race
  tracers, final frozen focused/project/solution Debug and Release, complete
  identity/source/runtime inventories, review and fresh exact-SHA Windows/macOS/
  Linux CI/CodeQL shall pass. Actual one-argument Block lifetime execution and
  healthy task-owned macOS Capture regression are separate opt-in native gates.

## Non-goals and evidence limits

Keep `delegate=0`, macOS 14.2 ordinary-arm64 candidate admission, Protection
Unknown and production sharing unavailable. No nonzero delegate, process-global
native-work admission, bridge/tag runtime, Capture permit integration, source-loss
acceptance, security-input/TCC/protection, physical LAN, release or v1 acceptance.
Portable after-effect simulation is not native exception/fault containment.
Finite native probe observations are not a guarantee about all SCStream copies
or future native callbacks; default probe execution shall remain non-native.
