# ADR 0028: Bounded Remote Window cleanup confirmation

- Status: Accepted implementation contract; three bounded verticals implemented,
  remaining implementation and evidence pending
- Date: 2026-08-30
- Decision owners: Flowspan maintainers
- First vertical: active terminal disconnect with one blocked cleanup owner
- Second vertical: external Dispose-first with one blocked Connection owner
- Third vertical: explicit Stop-first on one stable active generation
- Final v1 scope: runtime-generation and pre-generation host cleanup

## Context

[NR8](../../specs/v1/native-remote-window/requirements.md) requires bounded,
generation-safe lifecycle transitions, complete owner release, deterministic
failure preservation, and no reuse of resources still borrowed by an
asynchronous operation. The [Native Remote Window design](../../specs/v1/native-remote-window/design.md)
also requires frame admission to close before teardown and the complete managed
owner graph to drain.

[ADR 0027](0027-remote-window-host-preparation-reservation.md) establishes that a
host generation owns every Preparation reservation and later runtime owner. At
or after route admission, every terminal path shares one connection fail-close
and one complete cleanup task. ADR 0027 currently says concurrent Stop,
revocation, timeout, fail-close, callback, and Dispose callers join cleanup
completion. That is safe while every dependency cooperates, but it gives no
bounded answer when a native, transport, renderer, callback, or disposal owner
does not settle.

Cancellation is not a cleanup confirmation. Flowspan cannot release a buffer,
native use, callback, route, or authenticated connection merely because it asked
an owner to stop. Abandoning the cleanup task would allow a late operation to
touch released memory or a replacement generation. Waiting without a bound can
instead retain the coordinator lifecycle semaphore forever, preventing even a
fail-closed Start rejection or final application shutdown status.

The existing Desktop host shape also has an ownership split: explicit
`StopAsync` can await controller Stop before entering the generation's cached
cleanup task. A controller Stop that blocks can therefore bypass a watchdog
placed only around the later cleanup tail. The cached cleanup factory also
accepts a `controllerAlreadyStopped` value from whichever caller creates it
first, making first-caller scheduling part of cleanup semantics.

Finally, a public `DisposeAsync` task cannot both complete at a deadline and be
mutated later to deliver an asynchronous cleanup fault. A separate late-result
surface is required. Fatal exhaustion needs a separate rule as well: converting
an `OutOfMemoryException` into a bounded timeout or aggregate would contradict
the repository's fatal-dominates cleanup convention.

At decision time, the production-boundary matrix therefore kept CL Timeout
Missing. Existing
Preparation deadline, renderer cancellation, and test-side `WaitAsync` bounds
are not cleanup-timeout evidence because cleanup itself continues to be awaited
without a production confirmation watchdog.

## Decision

Flowspan will separate **real cleanup completion** from **bounded cleanup
confirmation**. The watchdog ends only a caller or coordinator wait. It never
cancels, replaces, abandons, or falsely completes the real cleanup task.

### Revision to ADR 0027

This ADR narrows ADR 0027's external join statement. Concurrent Stop,
revocation, fail-close, terminal callback, and external Dispose paths still join
one terminal transition and one real cleanup task. They join that task's
completion only while bounded cleanup confirmation remains pending. If the
watchdog wins, their bounded wait completes while the one real task continues
under the same owner generation.

ADR 0027's other ownership rules remain unchanged:

- frame admission closes before external cleanup waits;
- route-admitted and side-effect-then-throw paths consume the connection-owned
  media session and route;
- cleanup failure cannot skip a later independently safe owner step;
- one generation owns each reservation and runtime owner; and
- no failed or stale generation can grant capture, participant, Driver, input,
  media, or rendering authority.

### Complete real termination task

Each host termination has one synchronous authority-closing prefix followed by
exactly one real termination task. The initiating path closes frame admission
and removes the `RuntimeGeneration` from active authority if present, then
records it as retiring ownership before any await, watchdog arm, or task
publication. The real task then contains the whole controller and coordinator
teardown, including:

1. callback retirement and drain;
2. Preparation fact-reservation release;
3. controller Stop and any required fallback Stop;
4. Emergency Stop readiness and formal-registration release;
5. connection fail-close when the route may be owned;
6. media, control, protection, and authenticated-connection disposal; and
7. final Admission and remaining generation-owner release.

After an explicit Stop claims a generation, it may use its exact caller token
only for the first controller Stop attempt. The claimed generation's one real
task, confirmation operation, and watchdog are published before that attempt or
any other potentially blocking owner call begins. If the first attempt throws,
is cancelled, or returns `FullyStopped == false`, its original exception or
unconfirmed boundary result is retained as the terminal primary outcome. The
same real task then invokes exactly one owned fallback Stop with
`CancellationToken.None` and continues all later cleanup without caller
cancellation. A `FullyStopped == true` first result does not trigger a second
Stop. The caller token never controls the confirmation operation, fallback
Stop, or later owner release. The original thrown or cancelled outcome remains
the terminal primary after a successful fallback: later Start stays fail-closed
with `host_cleanup_unconfirmed`, and coordinator Dispose exposes that same
primary instance.

The current `controllerAlreadyStopped` first-caller input is not part of the new
contract. All callers observe one generation-owned termination result rather
than constructing different cleanup work based on which path arrived first.

### Active, retiring, and sticky-unconfirmed state

The coordinator represents terminal ownership with three distinct states:

- `active` is the only generation that may expose a sharing Snapshot or grant
  host authority;
- `retiring` retains the terminal generation, its not-yet-released owners, and
  its real termination task until that task settles; and
- a monotonic cleanup-unconfirmed latch records that bounded confirmation failed
  and denies every later Start on that coordinator.

Terminal initiation closes frame admission, removes the generation from
`active` if present, and records it as `retiring` before any await, watchdog arm,
or real-task publication. Completed cleanup steps may release their owners
normally; `retiring` prevents abandonment of unsettled owners rather than
artificially retaining objects already disposed. Late real completion may clear
the `retiring` reference but never clears the latch.

Start reads the latch and retiring state before it can select a route, send
Prepare, start capture, add a participant, publish media, or grant Driver input.
V1 has no automatic reset. A late successful cleanup permits eventual object
release, but recovery requires disposal and construction of a new coordinator.
An explicit external Dispose sets the coordinator's disposed gate before its
termination worker runs. A later Start on that object therefore keeps the normal
`ObjectDisposedException` projection instead of exposing
`host_cleanup_unconfirmed`; both projections occur before new authority.

### Watchdog policy and linearization

The cleanup-confirmation policy is process-local configuration with:

- a ten-second default duration;
- a positive duration no greater than a thirty-second hard maximum;
- one injected `TimeProvider`, defaulting to `TimeProvider.System`;
- at most one timer creation-and-arm attempt for each real cleanup operation;
- one stable timeout failure instance for that operation; and
- one confirmation completion shared by every waiter.

The duration is independent of Preparation TTL, owner-lease duration, protocol
deadlines, and caller cancellation. It begins when the real cleanup task starts.
A later waiter cannot reset, extend, replace, or add another timer.

Real completion and timer expiry race through one atomic state transition.
Cleanup wins only if its completion commits first. Otherwise expiry wins,
including exact deadline equality while cleanup is still uncommitted. The
losing path may update late diagnostics where applicable, but cannot change an
already published public result, start another cleanup task, or release an owner
twice. Confirmation continuations run asynchronously and timer callbacks do not
wait for the coordinator lifecycle semaphore.

### Timeout and restart projection

When expiry wins, the coordinator performs this order:

1. confirm frame admission is closed and `active` authority is absent;
2. confirm the same generation remains retained as `retiring`;
3. set the monotonic cleanup-unconfirmed latch;
4. record the stable bounded `host_cleanup_timeout` outcome; and
5. complete the shared confirmation wait.

The latch and outcome commit before the wait is released. A terminal callback
that holds the coordinator lifecycle semaphore can then release it at the
timeout without waiting for real cleanup. A later Start acquires the semaphore
and fails with `host_cleanup_unconfirmed`, even if real cleanup happens to finish
between timeout publication and that Start.

`host_cleanup_timeout` describes the bounded result of the wait that expired.
`host_cleanup_unconfirmed` describes the permanent restart denial. Neither
reason contains dependency, native, timer-provider, or peer-controlled text.

### Dispose and independent late observation

Public Dispose completion and real cleanup completion are deliberately separate.
All concurrent and later external Dispose calls share the same public Dispose
task. If that task completes with `host_cleanup_timeout`, it remains completed
with the same failure instance. A task that has already completed cannot later
be changed to deliver a cleanup fault.

The coordinator therefore retains and observes a separate internal real-cleanup
completion plus a terminal diagnostic ledger. This observation is not optional:
it prevents detached non-fatal faults and OOM from becoming unobserved. Dispose
called from an active generation callback retains its existing non-waiting
recursion behavior while still initiating or joining the same real task.

When external Dispose is the first terminal path, it sets the disposed gate
before returning. Once its disposal worker obtains lifecycle ownership of a
published active generation, the synchronous authority prefix closes frame
admission and atomically changes `active -> retiring` while publishing the
generation's one real cleanup task, confirmation operation, and watchdog. This
prefix completes before any potentially blocking controller or owner call. The
public Dispose task then awaits only bounded confirmation. If timeout wins,
concurrent and later external Dispose calls observe the same task and stable
`host_cleanup_timeout` instance; late real success or failure cannot mutate it.
A callback-origin Dispose call returns a non-waiting completed `ValueTask` to
avoid waiting on its own ancestry, but it starts or joins that same public
operation for later external callers.

A late successful cleanup releases every owner and budget but preserves the
timeout and sticky latch. A late non-fatal failure is recorded exactly once with
its original exception identity. It is visible through the real completion and
structured terminal diagnostics, but it does not retroactively mutate the
already returned public Dispose task.

### Deterministic failure ledger

Terminal projection uses semantic slots rather than thread-arrival order:

1. terminal primary failures, including explicit Stop or synchronous Emergency
   Stop failure;
2. cleanup-confirmation failure, either `host_cleanup_timeout` or
   `watchdog_unavailable`; and
3. watchdog disarm or release failure; and
4. real owner-cleanup failures in the fixed cleanup-step order.

Non-fatal aggregates are flat, retain their original exception instances, and
are created in that order. A timeout already returned to a public caller remains
stable; the ledger may subsequently expose the ordered timeout-plus-late-fault
diagnostic projection. Each generation records its real cleanup failure at most
once.

### Fatal exhaustion

A direct `OutOfMemoryException`, or the first OOM found inside a nested cleanup
aggregate, dominates the failure projection by its original instance. Flowspan
continues through independently safe later cleanup steps, but does not convert
that OOM into `host_cleanup_timeout`, `watchdog_unavailable`, another bounded
reason, or an `AggregateException`.

If OOM occurs before confirmation completes, the real completion exposes that
same instance. If it arrives after public timeout, the independently observed
real completion and fatal diagnostic lane expose the same instance. An already
completed public task is not mutable; requiring that task itself to rethrow a
later OOM would be impossible and is not this contract.

### Watchdog failure

If timer creation or arming throws a non-fatal exception, bounded confirmation
fails closed immediately with `watchdog_unavailable`. The sticky latch is set,
real cleanup remains owned and observed, and raw provider text is restricted to
the internal diagnostic ledger. Provider OOM follows the fatal rule.

Timer disarm or disposal does not delay a confirmation already published. A
non-fatal timer release failure is observed as a cleanup fault in its fixed
ledger position; an OOM release failure follows fatal dominance. A late timer
callback after cleanup won cannot change the winner or publish timeout.

### Pre-generation cleanup

The final v1 contract also covers resources acquired before a
`RuntimeGeneration` exists. Validation and construction can already own a
connection, protection source, controller, admission sink, or media sink. Their
cleanup will use an equivalent transient cleanup operation with one real task,
one confirmation operation, at most one non-extensible watchdog arm attempt,
exactly one timer after a successful arm, retained ownership through late
settlement, independent late diagnostics, fatal dominance, and the same
permanent restart denial after unconfirmed cleanup.

Until this pre-generation path is implemented and tested, it remains an explicit
Task 5.5a sub-boundary gap. Releasing the runtime-generation semaphore at timeout
does not by itself prove that a subsequent rejected Start is bounded if cleanup
of that Start's newly supplied, pre-generation resources can block.

### First implementation checkpoint

The first implementation checkpoint is intentionally one narrow production-
composed vertical:

1. a managed two-node host reaches active final Admission over real
   authenticated protocol 1.7 and `FSM1` ownership;
2. independent authenticated terminal disconnect starts the host generation's
   production cleanup;
3. one injected real cleanup owner blocks deterministically after cleanup has
   entered;
4. a manual `TimeProvider` advances the production watchdog to exact equality;
5. the coordinator records `host_cleanup_timeout`, releases its lifecycle
   semaphore, and rejects a replacement Start with
   `host_cleanup_unconfirmed` and zero new authority;
6. the blocked owner is released, the same real task drains both nodes; and
7. restart remains rejected after late successful cleanup.

This checkpoint may advance only CL Timeout from Missing to Partial. It does not
cover explicit Stop or Dispose initiation, cleanup completion winning the race,
pre-generation cleanup, timer setup or release failure, late cleanup fault, OOM,
other active or pending owners, combined failures, native API behavior, or
physical devices. Those paths remain open and the checkpoint cannot close Task
5.5a or make `CreateProduction()` available.

### External Dispose-first implementation checkpoint

Implementation commit `ea984fb01cad46ab128c6d294835df59327aa8ac`
implements the first Task 5.5a.3 extension for one stable active generation and
an uncontended lifecycle gate. The first external Dispose sets the disposed
gate. After its worker acquires lifecycle ownership, the synchronous terminal
prefix closes admission and publishes `active -> retiring`, the one real cleanup
task, confirmation operation, and watchdog before any potentially blocking
controller or owner call.

The deterministic row blocks the original host Connection disposal, injects a
later authenticated-disconnect callback, and proves that callback may execute
its existing synchronous Emergency Stop prefix but attaches cleanup exactly once
to the already-published operation. T-1 remains pending. Exact deadline equality
publishes one stable `host_cleanup_timeout`; concurrent, later, and post-drain
external Dispose callers share the same public Task and exception instance.
Start preserves `ObjectDisposedException` precedence with no new authority.
Releasing the Connection drains the real task and timer without mutating the
public result.

Local Debug and Release verification passes the focused row `1/1`, twenty fresh
focused processes `20/20`, the coordinator class `117/117`, Desktop `721/721`,
and the solution `2585/2585`. Exact-SHA CI `33314229467` and CodeQL
`33314229459` succeed; all three hosted OSes pass `2585/2585`, Gitleaks reports
208/0, CodeQL reports 52/0 with zero exact-ref open alerts, and all three
reproducible version-`0.1.222` unsigned-package jobs pass. Exact jobs, artifacts,
digests, commands, and limitations are retained in the
[Dispose-first evidence](../evidence/2026-08-30-dispose-first-bounded-cleanup.md).

This checkpoint closes only Task 5.5a.3a. It adds no 43rd production-composed
tracer row and does not promote CL Timeout beyond Partial. Stop-first,
lifecycle-gate contention, completion-winner/equality races, timer faults, late
cleanup fault/OOM, pre-generation cleanup, every other owner, native/physical
execution, signing, notarization, and release acceptance remain open. It cannot
close Task 5.5a or make `CreateProduction()` available.

### Explicit Stop-first implementation checkpoint

Implementation commit `681842290d44f9524eab33550b307bad76017fbc`
completes Task 5.5a.3b as a narrow portable coordinator slice for one stable
active generation and an uncontended lifecycle gate. Stop-first claims the
generation, closes Admission, and publishes `active -> retiring`, the one real
cleanup task, one confirmation operation, and one watchdog before invoking
controller Stop or any other potentially blocking owner. The exact caller token
is passed only to the first controller Stop attempt.

Two deterministic, low-risk scenarios freeze this boundary. In the first, the
caller token is cancelled after publication and before the confirmation
deadline. The first Stop observes that exact cancellation, the real task retains
it as the primary outcome, invokes exactly one fallback Stop with
`CancellationToken.None`, and finishes cleanup without turning caller
cancellation into cleanup cancellation. Public Stop and later coordinator
Dispose expose the same cancellation instance, while restart on that coordinator
remains fail-closed.

In the second, the first Stop remains blocked through T-1 and exact deadline
equality. Timeout wins confirmation while the same task and generation remain
retiring; releasing the Stop later returns `FullyStopped == true`, performs no
fallback Stop, and lets that same real task drain without changing the published
timeout.

Local Debug and Release verification passes the two focused rows `2/2`, twenty
fresh focused processes per configuration with `40/40` case executions,
coordinator `119/119`, Desktop `723/723`, and solution `2587/2587`. Exact-SHA
CI `33317026854` and CodeQL `33317026837` succeed; all three hosted OSes pass `2587/2587`, Gitleaks
reports 208/0, CodeQL reports 52/0 with zero exact-ref open alerts, and all three
reproducible version-`0.1.224` unsigned-package jobs pass. Exact jobs, artifacts,
digests, commands, and limitations are retained in the
[Stop-first evidence](../evidence/2026-08-30-stop-first-bounded-cleanup.md).

This checkpoint closes only Task 5.5a.3b. It adds direct evidence within the
already-Partial CL Cancel and CL Timeout cells, adds no 43rd
production-composed tracer, and changes no matrix status. It does not freeze
race precedence among concurrent Stop, Dispose, and callback initiators;
ordinary throw and `FullyStopped == false` combinations; lifecycle-gate
contention; cleanup-completion winner and equality races; timer creation, arm,
release, or callback faults; late cleanup failure or OOM; pre-generation
cleanup; or a blocked owner other than controller Stop. Those remain later Task
5.5a.3 slices. Tasks 5, 5.5a.3, 5.5a, and 5.5, every native, physical, signing,
notarization, and release gate, and the Goal remain open. `CreateProduction()`
remains unavailable.

## EARS acceptance criteria

1. **When** any terminal path claims a host generation, **Flowspan shall** close
   frame admission before arming a watchdog or awaiting an owner.
2. **When** a generation first enters termination, **Flowspan shall** create
   exactly one real termination task, one confirmation deadline, at most one
   timer-arm attempt, and one stable timeout failure instance for every joining
   caller; a successful arm shall own exactly one timer.
3. **If** real cleanup commits before the timer callback, **Flowspan shall**
   publish the existing Stop, success, or cleanup-failure semantics without
   setting the timeout latch.
4. **When** timer expiry commits first, **Flowspan shall** publish
   `host_cleanup_timeout`, set the cleanup-unconfirmed latch, and stop only the
   confirmation wait without cancelling or abandoning real cleanup.
5. **Before** a timeout wakes a terminal callback waiter, **Flowspan shall**
   remove active authority and set the latch so the lifecycle semaphore can be
   released safely.
6. **While** the cleanup-unconfirmed latch is set on a coordinator that has not
   entered explicit disposal, **when** Start is attempted, **Flowspan shall**
   return `host_cleanup_unconfirmed` before any route, Prepare,
   capture, Admission, media, rendering, or Driver authority. A Start that arrives
   while cleanup is merely retiring and confirmation is still pending remains
   serialized by the lifecycle semaphore; cleanup success may then proceed,
   whereas timeout sets the latch before that semaphore is released. **After**
   explicit Dispose sets the disposed gate, Start instead throws
   `ObjectDisposedException` before granting authority.
7. **When** real cleanup succeeds after timeout, **Flowspan shall** eventually
   release every owner and budget while preserving the timeout and latch.
8. **When** real cleanup fails non-fatally after timeout, **Flowspan shall**
   observe the original failure exactly once after the timeout in deterministic
   ledger order without mutating a completed public Dispose task.
9. **When** any direct or nested cleanup result contains OOM, **Flowspan shall**
   expose the first original OOM instance on the real completion and fatal
   diagnostic lane without converting it to a bounded reason or aggregate.
10. **When** Stop, revocation, fail-close, terminal callback, and Dispose race,
    **Flowspan shall** share one terminal transition and real task. An explicit
    Stop that owns the initial controller attempt shall pass its exact caller
    token only to that attempt, invoke exactly one `CancellationToken.None`
    fallback when the attempt throws, is cancelled, or returns
    `FullyStopped == false`, and skip fallback after `FullyStopped == true`.
    Flowspan shall share exactly one fail-close task when a route may be owned
    and attempt each acquired owner's release exactly once.
11. **When** cleanup completion and watchdog expiry race, **Flowspan shall**
    commit exactly one winner and prevent the losing path from changing public
    state or releasing an owner twice.
12. **If** watchdog creation or arming fails non-fatally, **Flowspan shall**
    publish `watchdog_unavailable`, block restart, and continue observing real
    cleanup; **if** it fails with OOM, Flowspan shall preserve the fatal instance.
13. **When** multiple external Dispose calls overlap or follow a timeout,
    **Flowspan shall** return the same public Dispose completion and stable
    timeout instance while exposing late cleanup only through the separate real
    completion and diagnostics. A callback-origin Dispose may return without
    waiting, but shall initiate or join that same operation.
14. **When** late cleanup settles in v1, **Flowspan shall not** automatically
    reset the cleanup-unconfirmed latch or reactivate that coordinator.
15. **Before** Task 5.5a closes, **Flowspan shall** apply equivalent bounded
    confirmation and late-owner retention to pre-generation cleanup.

## Rejected alternatives

### Cancel real cleanup at the watchdog deadline

Rejected. Cancellation is a request, not confirmation that native callbacks,
borrowed buffers, routes, or authenticated owners have stopped using their
resources.

### Clear owners and permit restart after timeout

Rejected. A late generation could act on disposed or replacement state. The
retiring generation retains unsettled ownership and the sticky latch blocks all
restart on that coordinator.

### Create a watchdog for each caller

Rejected. Later callers could extend the security boundary, report different
outcomes, or create timer pressure. One generation owns one non-extensible
confirmation deadline.

### Hold the lifecycle semaphore until physical cleanup completes

Rejected. A non-cooperative owner could prevent a bounded fail-closed Start
rejection and leave terminal coordinator progress unobservable.

### Report late failure by changing the completed Dispose task

Rejected because task completion is immutable. A separate always-observed real
completion and diagnostic ledger are required.

### Aggregate OOM with timeout or cleanup failures

Rejected. Fatal exhaustion dominates by exact instance; wrapping it would change
the repository's fatal-cleanup semantics and could misclassify it as a product
timeout.

### Treat one tracer as complete CL Timeout coverage

Rejected. The first vertical covers one terminal-disconnect order and one blocked
owner only. It can establish direct production evidence and move Missing to
Partial, but not cover the remaining lifecycle initiators, owners, races, and
failure combinations.

## Non-goals

- Preempting or forcibly terminating arbitrary native, renderer, transport, or
  third-party code.
- Claiming that `host_cleanup_timeout` means physical resources have already
  drained.
- Resetting or reusing a timed-out coordinator in v1.
- Changing protocol 1.7 wire messages, Preparation deadlines, media budgets, or
  owner-lease durations.
- Making production Remote Window available.
- Treating managed loopback as native API, physical-device, signing,
  notarization, accessibility, or release evidence.
- Promoting CL Timeout beyond Partial from the first vertical.

## Consequences

- Public lifecycle waits become bounded without weakening ownership of
  non-cooperative cleanup.
- The coordinator must retain a retiring generation and observe real completion
  after its public timeout.
- Explicit controller Stop moves into the single generation-owned real task.
- Terminal failure storage becomes a deterministic semantic ledger rather than
  an arrival-ordered nested aggregate.
- Cleanup helpers must adopt fatal-dominates OOM behavior.
- Pre-generation cleanup requires the same bounded-confirmation abstraction
  before Task 5.5a can close.
- The first vertical changes only one Missing matrix cell to Partial; all other
  managed, native, physical, packaging, and release gates remain open.
