# Generation-routed macOS stream delegate

Status: implementation contract for a native-capture candidate under NR6,
NR8 and NR10. This is not production Remote Window admission or full source-loss
acceptance. The existing Capture remains `delegate=0` until the integration
and lifetime gates below have evidence.

As a maintainer, I need stream-terminal events to reach only their original
Capture, including before the first sample, without retaining an unbounded
number of per-session delegate bridges or inventing a native-drain guarantee.

## Acceptance criteria

- MSC1: When the native candidate publishes a delegate, it shall use one
  permanently +1-owned, process-rooted bridge with no per-Capture state. Each
  stream shall carry an immutable, retained associated tag containing only a
  positive numeric generation. Generations shall never be reused, including
  after successful cleanup; exhaustion shall reject construction.
- MSC2: While constructing, active, retiring or quarantined Captures exist,
  the candidate shall hold at most 16 Capture permits. It shall allow at most
  one initializer, reject contention without waiting, and reserve before native
  allocation. Managed retirement alone shall not return a Capture permit;
  confirmed complete Capture cleanup is required. Native tag allocations shall
  have a separate maximum-16 budget, returned only after actual tag superclass
  deallocation completes successfully, not merely upon dealloc entry.
- MSC3: When a terminal callback occurs before association publication, the
  native adapter shall retain its valid borrowed stream before storing an early
  fact, snapshot the exact initializer, and re-read association to close the
  nil-tag/publication/pending-clear race. It shall not lose a terminal event or
  route an unknown old stream to a later initializer. If exact-initializer
  early-fact recording loses to publication/clear, it shall re-read association
  and route only a verified immutable tag, or preserve fail-closed ambiguity;
  it shall never snapshot a replacement initializer. Published initialization
  with an unproved source identity shall poison later construction until
  process restart unless a stronger absence proof is established. Repeated
  early events for the same retained stream may coalesce; a distinct untagged
  stream during that initialization shall poison rather than overwrite the
  early fact. Every retained pending fact shall be released on exact completion
  or rejection, or remain charged to an explicit quarantine when release is
  unconfirmed. Completion shall compare its returned source with the retained
  early source and revalidate the exact token after reentrant publication.
  Only terminal facts may occupy/coalesce pending storage; Active shall not
  consume or replace them.
- MSC4: When an exact current generation receives StoppedWithError or Inactive,
  it shall close local delivery admission before notifying SourceUnavailable
  once, even without samples. Active and successful Start completion shall not
  reopen terminal admission. Unknown and retired generations shall affect no
  other Capture by generation routing. A process-wide native ownership/resource
  failure is a distinct fail-closed runtime event governed by MSC9, not a
  terminal signal guessed from an unknown generation.
- MSC5: When retirement begins, it shall close admission, join admitted managed
  handlers and remove the generation-to-Capture mapping. Direct and active
  ExecutionContext-descendant self-join shall reject before waiting. It shall
  leave the stream tag associated until stream deallocation; retirement shall
  never be named or treated as native delegate drain.
- MSC6: While routing, no managed state gate shall be held across native calls,
  external handlers, waits or completion continuations. Reverse entries shall
  contain managed exceptions, preserve the original fatal failure, restore
  ancestry and release admitted invocation ownership in finally. Native
  references acquired by a callback shall remain valid through handler reentry
  and be balanced afterward.
- MSC7: Before replacing Capture's `delegate=0`, the implementation shall
  distinguish delegate publication from sample-output publication, exercise the
  actual Capture composition, and join managed delegate handlers before native
  resource release. Existing Block/sample-output lifetime proof remains
  independent. Failed or uncertain cleanup shall retain the full Capture and
  its permit, without granting production availability.
- MSC8: Before claiming native tag lifetime evidence, an opt-in no-capture
  Foundation probe shall actually execute association reads, retained callback
  references, immutable generation routing and ordinary-arm64 superclass
  deallocation in Debug and Release. Default execution shall make no native
  calls. Actual source-close/SCStream failure is a separate opt-in test, with
  native sample count distinguished from sink-frame count.
- MSC9: Before native source/tag work, the coordinator shall reserve one of
  at most 16 independent process ownership records, separate from Capture and
  tag-allocation permits. In-flight callbacks, retained early facts, association
  publication and uncertain retain/read/release outcomes shall remain charged
  until every owned reference is confirmed released. Unconfirmed release shall
  never be retried blindly. Identity ambiguity shall poison new construction
  and quarantine only related exact tokens without notifying another Capture.
  Native ownership/resource faults, including ownership-record exhaustion,
  shall permanently close new native-work and all delivery admission and
  preserve the original primary/fatal failure. Already admitted native work may
  return; known independent references shall still receive at most one cleanup
  attempt outside managed gates. Late uncertainty shall remain in process
  ownership records and shall not reoccupy a complete-cleanup registration's
  reused Capture slot. Actual Capture composition shall consume this runtime
  failure/admission state before it can be considered verified.

## Non-goals and evidence levels

The new three-signal composition initially targets ordinary-arm64 macOS 15.2+;
it does not silently extend the existing 14.2 candidate claim. Intel, arm64e,
minimum-OS, TCC, protection, input, independent Emergency Stop, physical-device,
packaged, signing and release gates remain open. Portable router tests prove
only managed behavior. Unlike MDO6's opaque-pointer prerequisite, native
generation routing reads stream/tag associations; it must not inherit a
"no native read" claim. No arbitrary application-state migration is promised.
