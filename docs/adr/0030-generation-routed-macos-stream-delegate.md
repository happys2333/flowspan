# ADR 0030: Generation-routed macOS stream delegate

- Status: accepted for staged implementation; native composition unverified
- Date: 2026-10-05
- Requirements: NR6, NR8, NR10; MSC1-MSC8

## Context

The MDO prerequisite proves bounded managed callback ownership using per-bridge
immutable addresses, but published bridges consume its finite pool forever.
That deliberate probe retention is not sustainable per-session product
ownership. Public SCStream APIs expose no delegate setter/unbind operation.
Stop completion promises stream stopping, not cessation of all future delegate
callbacks; sample queue barriers apply only to SCStreamOutput.

## Decision

Stage one permanent process-owned NSObject bridge, an immutable stream-associated
numeric generation tag, and a bounded managed generation registry. Retire the
managed handler without removing the native association; late callbacks read
the old generation and cannot target a replacement. Never reuse generations.
The native router is process-rooted and cannot be replaced or reset after
publication.
Hold separate maximum-16 Capture and tag allocation budgets; managed handler
retirement does not release either native-lifetime obligation. Serialize only
initialization admission, fail fast on contention, and poison future
initialization after ambiguous published failure.

Implement and verify the portable router first, then actual no-capture
association/dealloc ABI, then actual Capture composition. Keep existing Capture
at `delegate=0` until those prerequisites pass. The new Inactive/Active
composition starts at ordinary-arm64 macOS 15.2, not the older 14.2 floor.

## Consequences

One permanent bridge is bounded process retention, not delegate cleanup.
Associations and retained borrowed callbacks introduce native reads and ownership
obligations absent from MDO's opaque-pointer proof. Unknown early streams need
retained identity and a publication-race protocol; ambiguous failure is a
process-lifetime fail-closed limitation. Existing sample-output address and Block
lifetime proofs remain separate. No native-drain claim follows from finite
late-callback observations or managed retirement.

## Sources and provenance

Independent C# implementation; no Deskflow code copied. Public Apple contracts:

- [Associative references](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/ObjectiveC/Chapters/ocAssociativeReferences.html): retained associations follow source lifetime.
- [Practical memory management](https://developer.apple.com/library/archive/documentation/Cocoa/Conceptual/MemoryMgmt/Articles/mmPractical.html): borrowed references are not caller-owned +1 references.
- [Clang ARC retainable pointer operands](https://clang.llvm.org/docs/AutomaticReferenceCounting.html#retainable-object-pointers-as-operands-and-arguments).
- Local Xcode SDK `ScreenCaptureKit.framework/Headers/SCStream.h`: initializer,
  sampleHandlerQueue, Stop completion, and delegate methods; Inactive/Active are
  macOS 15.2 APIs. These declarations do not establish delegate drain.

See [requirements](../../specs/v1/native-remote-window/macos-stream-delegate/requirements.md),
[design](../../specs/v1/native-remote-window/macos-stream-delegate/design.md) and
[tasks](../../specs/v1/native-remote-window/macos-stream-delegate/tasks.md).
