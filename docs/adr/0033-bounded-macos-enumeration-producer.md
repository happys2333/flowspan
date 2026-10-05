# ADR 0033: Bounded macOS enumeration producer

- Status: accepted for staged implementation; acceptance unverified
- Date: 2026-10-05
- Requirements: MEP1-MEP8; NR8/NR10; MSC6/MSC9 prerequisites

## Context

The initial source producer is bounded at `5f62eb8`, but EnumerateCoreAsync can
still lose retained content after an effect throws before assignment, return its
batch, skip independent cleanup or mistake a first callback latch for drain.
MacOSRemoteWindowBlock also has internal factory and retry/finalizer contracts;
an outer wrapper cannot prove those partial debts contained.

## Decision

Keep one real enumeration orchestration with injectable system effects. Attach
its ledger and complete managed graph to the original reserved BatchRecord
before ownership effects. Use the existing8-batch/source pool, not a second
unbounded resource framework. Unknown effects remain charged without guessing
release/retry. Result admission, invocation exit, owned Block release and native
capture retirement are separate facts. Initial SourceRecords and CreateSourceCore
stay unchanged and are not double charged.

Implement content, dispatch/list/callback and actual primitive Block obligations
as behavioral vertical tracers. Primitive factory/native-copy drain are required
acceptance, not optional future work hidden by a successful content tracer.
Native calls, observers and joins occur outside gates; exact immutable context/
ledger identity protects replacement batches. Diagnostics preserve nested fatal
identity internally while ordinary outward reports remain bounded/redacted.

## Consequences and provenance

Unknown native lifetime may consume finite capacity until restart; no timeout,
finalizer or cached callback success authorizes return. Fresh-pool GC is managed
graph proof only. Production remains delegate=0,14.2/Arm64 candidate and sharing
unavailable. This independently written C# decision reuses this repository's
ownership contracts and documented native concepts, not Deskflow GPL code or a
new production dependency. See the
[requirements](../../specs/v1/native-remote-window/macos-enumeration-producer/requirements.md),
[design](../../specs/v1/native-remote-window/macos-enumeration-producer/design.md)
and [tasks](../../specs/v1/native-remote-window/macos-enumeration-producer/tasks.md).
