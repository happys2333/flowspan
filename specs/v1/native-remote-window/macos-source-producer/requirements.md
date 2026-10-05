# macOS initial source-producer ownership

Status: approved v1 fail-closed baseline; implementation/local verification
complete, fresh hosted gates pending after the
SourceEntry/Catalog checkpoint `3aa099b`. Fresh hosted verification of that
earlier checkpoint passes separately, not for this new work. Trace: NR8/NR10, MSC6/MSC9.

As a source host, I need initial source creation to keep partial native ownership
durably charged even when an acquisition effect throws before its return value
is assigned. The catalog cannot recover an owner the producer never returned.

- MSP1: Before CreateSource attempts its first owned allocation, the producer
  shall claim one pre-reserved source record and attach its complete acquisition
  ledger. Exhaustion shall reject before that effect. Catalog invocation shall
  use its existing reserved batch; direct internal callers shall reserve from
  the same bounded pool, not an independent unlimited ledger.
- MSP2: When allocation, initialization or initial window retain throws after
  its attempt begins, the producer shall preserve attempted/confirmed facts,
  retain uncertain debt without retry or guessed release, and prevent the batch
  settlement path from returning that record. The exact producing context shall
  close new admission before that failure escapes; this is not global closure
  of every existing Capture. Initialization consumption and
  returned-self semantics shall be explicit rather than inferred from zero.
- MSP3: When creation fails, independent confirmed owners shall each receive
  at most one cleanup attempt outside state gates. Original nested fatal identity
  shall survive later cleanup failure; ordinary failures shall use a bounded
  diagnosis without native identities or window metadata.
- MSP4: When creation succeeds, the producer shall hand the same source record
  through real NativeSource to SourceEntry exactly once, without double charging
  or losing the ledger between effects, construction, list insertion and entry
  attachment. Only complete confirmed cleanup may return its charge.
- MSP5: While an initial acquisition remains uncertain, its full owner/effects
  graph shall remain reachable through the actual bounded pool after caller,
  context and task references leave scope. GC tests shall use fresh real pools,
  not contaminate/reset production quarantine or claim native lifetime proof.
- MSP6: Before this slice closes, actual behavioral RED→GREEN, focused/project
  and complete solution D/R, source/runtime/qualified inventories, single-layer
  review, selected healthy native regression, and fresh exact-commit all-OS
  CI/CodeQL evidence shall pass. No old checkpoint outcome transfers to new code.

Non-goals: EnumerateAsync content retain, Block/dispatch/callback/list-level
ownership, transient native query-resource faults hidden inside current checks,
global Capture admission, nonzero delegate, production sharing, protection/input,
physical Devices or release acceptance. Existing Capture retained-source tokens
own separate native retain obligations; their staged algorithm is not replaced.
Delegate=0, macOS14.2/Arm64 candidate admission and production availability stay.
