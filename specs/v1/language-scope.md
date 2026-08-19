# Flowspan v1 Language Scope

Status: approved baseline

## Implementation language

Flowspan v1 uses C# on .NET as its single implementation language across the
portable core, protocol, security, transport, platform adapters, desktop shell,
tests, and delivery tooling. Native operating-system APIs may be reached through
.NET interop, but a second maintained implementation language is not introduced
unless a concrete platform requirement cannot be met safely and maintainably in
C#. Any such exception requires a separate ADR and review of its build, test,
security, licensing, and long-term maintenance cost.

The durable rationale and alternatives are recorded in
[ADR 0001](../../docs/adr/0001-dotnet-single-language.md).

## User-interface language

Flowspan v1 ships one maintained user-interface language: neutral English. It
does not ship culture-specific translations, a language selector, or runtime
language switching. User-visible and accessibility-only desktop text is still
externalized into neutral resource catalogs so that wording has one reviewable
boundary and future translations do not require presentation control-flow
changes.

Displayed numbers, dates, and times use the current display culture. Protocol
versions, capability identifiers, reason codes, schema and JSON member names,
paths, file names, hashes, identifiers, and ISO-8601 diagnostic timestamps are
machine contracts and remain invariant. User-controlled values are format
arguments, never resource keys.

The resource boundary and its verification rules are specified in the
[Desktop Quality requirements](desktop-quality/requirements.md) and
[ADR 0023](../../docs/adr/0023-neutral-desktop-resource-catalog.md).

## Deferred scope

Additional maintained translations, pluralization policy, translator workflow,
satellite-resource packaging, locale-specific layout review, and runtime
language switching are post-v1 work. Their absence does not relax v1 keyboard,
accessible-name, text-scaling, or externalization requirements.
