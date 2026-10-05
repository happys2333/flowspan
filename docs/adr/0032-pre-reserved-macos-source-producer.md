# ADR 0032: Pre-reserved macOS initial source producer

- Status: accepted for staged implementation; verification pending
- Date: 2026-10-05
- Requirements: MSP1-MSP6; NR8/NR10; MSC6/MSC9 prerequisites

## Context

CreateSource currently combines filter alloc/init, then assigns the initial
window retain return value before constructing NativeSource. A native effect
may change ownership and throw before assignment. Catalog cannot retain that
unreturned owner, and a local finally/raw address is not durable uncertainty
accounting. Adding another unlimited quarantine would hide rather than bound it.

## Decision

Use a thin system-effects seam around the same production creation orchestration.
Split allocation and initialization effects; explicitly track attempted and
confirmed ownership/consumption. Attach the acquisition token to one already
reserved source slot before its first owned effect. Pass an explicit internal
batch context rather than ambient or thread-local last-failure state.

Transfer the same record through real NativeSource and SourceEntry; do not take
another catalog slot for the same base ownership. Direct internal enumeration
callers obtain finite envelopes from the same process pool. Confirmed owners
receive independent cleanup attempts outside state gates; unknown effects stay
rooted/charged without retry or guessed release. Original nested fatal wins over
later cleanup failures. Capture's additional retained-source copy remains a
separate obligation and is not changed into the producer's base token.

Direct envelopes reuse the owner-record, batch and source capacities. A stable
failure-sink object is captured by each exact context; it never dynamically
looks up the reused batch's owner. Healthy settlement returns unused slots but
retains every live source, then closes the direct owner. Its last confirmed
source cleanup returns that owner. A failed acquisition keeps the active batch;
late cleanup failure after settlement retains only its original source/owner.

The effect interface explicitly follows the Objective-C init-family consuming
receiver contract: normal return consumes the allocation +1 and returns either
the sole owned self (possibly replaced) or nil. Nil allocation skips init;
throw confirms neither receiver consumption nor output. Basis:
[Clang init semantics](https://clang.llvm.org/docs/AutomaticReferenceCounting.html#semantics-of-init)
and [Apple initialization guidance](https://developer.apple.com/library/archive/documentation/General/Conceptual/CocoaEncyclopedia/Initialization/Initialization.html).
This is ownership documentation, not copied implementation or proof of native
exception recovery; no new production dependency is introduced.

## Consequences and provenance

Uncertainty consumes finite capacity until restart; full graph retention is
intentional, not permission to evict debt. Fresh-pool GC tests prove managed
reachability only. Native content/Block/dispatch/callback ownership, query-internal
resources, global admission and complete nonzero-delegate composition remain
separate. Candidate availability and platform floor do not change.

Independent C# implementation based on this repository's ownership contracts;
no Deskflow implementation or new production dependency is introduced. See
[requirements](../../specs/v1/native-remote-window/macos-source-producer/requirements.md),
[design](../../specs/v1/native-remote-window/macos-source-producer/design.md)
and [tasks](../../specs/v1/native-remote-window/macos-source-producer/tasks.md).
