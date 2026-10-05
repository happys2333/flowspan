# ADR 0031: Bounded macOS source-entry ownership

- Status: accepted; local verification complete, fresh hosted verification pending
- Date: 2026-10-05
- Requirements: NR8/NR10; SCE1-SCE6; MSC6/MSC9 prerequisites

## Context

The Capture-retained NativeSource token is rooted before native retain, but a
catalog's base source has no Capture root. Catalog instance failure fields and
completed Task cycles can be collected after the caller drops the catalog.
SourceEntry's last binding can release after retirement; its failure currently
bypasses the catalog's cleanup-failure admission state. A failed native release
may already have consumed ownership and cannot safely be retried.

## Decision

Notify the exact catalog cleanup owner from the real entry's final release,
before returning an unconfirmed outcome. Retain that complete graph through
fixed pre-reserved process ownership, separately budgeted from Capture,
association records and tag allocations. Initial engineering defaults are
8 catalog lifetimes, 1024 source obligations and 8 batch records; the producer
batch remains bounded to 128. Reserve before enumeration, fail closed on
exhaustion and return only independently confirmed obligations.

Use a small internal pool, not a generic resource framework. State selection,
failure linking and budget transfer stay under short gates; native cleanup,
invalidation handlers and waits stay outside them. Preserve independent cleanup
and original fatal identity. A held binding outlives catalog disposal and keeps
its exact charge. Unknown cleanup permanently retains its charge without reset.

## Consequences and provenance

Quarantine deliberately consumes finite capacity until process restart and must
produce truthful bounded degradation, not silently evict an owner. Root count
alone does not prove bounded subgraphs; source/batch obligations are counted
separately. Portable tests inject fresh bounded pools, never reset production
quarantine. No new dependency is introduced.

Independent C# design informed by the repository's existing ownership contracts;
no Deskflow code is copied. This catalog root cannot recover a native owner lost
inside CreateSource/EnumerateAsync before return; those entry points and global
delegate admission remain separately open. See
[requirements](../../specs/v1/native-remote-window/macos-source-entry-ownership/requirements.md),
[design](../../specs/v1/native-remote-window/macos-source-entry-ownership/design.md)
and [tasks](../../specs/v1/native-remote-window/macos-source-entry-ownership/tasks.md).
