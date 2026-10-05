# macOS enumeration-producer ownership

Status: executing the approved v1 fail-closed baseline. This follows the exact
`5f62eb8` initial source-producer checkpoint; none of its outcomes transfers to
new implementation. Trace: NR8/NR10, MSC6/MSC9 ownership prerequisites.

As a Remote Window source host, I need an enumeration failure to keep every
partial native obligation accounted for and to stop new work without guessing
that a callback exit, thrown effect or empty result proves cleanup.

- MEP1: Before an enumeration attempts its first owned effect, the real
  orchestration shall attach its complete operation graph to its exact already
  reserved BatchRecord. Catalog and direct calls shall share the existing
  bounded pool; stale or closed contexts and exhausted capacity shall reject
  before an ownership effect. No independent unbounded quarantine is permitted.
- MEP2: When content retain begins, its borrowed input and attempted state
  shall already be rooted. A throw or invalid return shall close the exact
  producing context and preserve unknown content debt without guessed release,
  retry or batch return. Confirmed content receives at most one release attempt.
- MEP3: When completion construction, dispatch or autorelease-pool ownership
  begins, their attempted/confirmed facts and full owner graph shall remain
  durable across after-effect failure. A factory-local owner lost before return
  shall not be described as contained merely because an outer wrapper is rooted.
- MEP4: When completion callbacks repeat, overlap, arrive late or re-enter
  settlement, only one admitted result shall acquire content ownership; further
  callbacks shall not create additional retain debt. Every admitted callback
  shall be tracked until its managed invocation exits. Managed callback exit,
  owned-block release and physical native-copy drain shall be distinct facts.
- MEP5: When any enumeration or cleanup step fails, cleanup of independently
  confirmed sources, content, pools and owned Block shall each be selected once
  and attempted outside state gates. One failure shall not skip other independent
  obligations or replace an earlier nested fatal. Ordinary outward diagnostics
  shall be bounded and contain no native identities or window metadata.
- MEP6: While any effect or callback/native lifetime remains uncertain, the
  original batch and complete graph shall remain rooted and charged through
  settlement and GC. Only complete confirmed cleanup and native/managed lifetime
  proof may return the enumeration's charge. Old ledgers or callbacks shall not
  mutate, poison or return a reused replacement batch.
- MEP7: When enumeration succeeds, actual initial source creation shall keep
  using CreateSourceCore and its same source records. Enumeration ownership
  shall settle independently from live source/entry obligations, without a
  second source-slot charge, premature source return or false publication.
- MEP8: Before this slice closes, actual behavioral RED→GREEN, focused/project
  and full-solution Debug/Release, exact qualified/source/runtime inventories,
  fault/re-entry/concurrency contracts, single-layer review, selected healthy
  task-owned native regression and fresh exact-SHA all-OS CI/CodeQL evidence
  shall pass. Compiler candidates and simulation cannot stand in for native proof.

Boundary: implement the full bounded enumeration path, not just a test-only
content owner. Native completion factory/Block primitive debts remain open
until task4 actually proves them; a content-only checkpoint cannot close MEP3,
MEP4, MEP6 or this slice. Nonzero SCStream delegate, global Capture admission,
query-internal temporary resources, TCC/secure-input/protection, physical LAN,
production host availability and signed release remain subsequent gates.
Candidate platform floor, delegate=0 and Protection Unknown stay unchanged.
