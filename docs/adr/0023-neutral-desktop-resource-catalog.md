# ADR 0023: Use neutral resources for desktop presentation text

- Status: Accepted
- Date: 2026-08-19
- Decision owners: Flowspan maintainers

## Context

The v1 desktop shell ships one maintained language, but R10.5 requires every
user-visible string to be externalizable. Static text currently lives in
Avalonia XAML while dynamic status and result prose lives in desktop view
models. Flowspan also needs a regression gate that covers accessibility-only
text without pulling localization concerns into domain or protocol assemblies.

## Decision

Use embedded neutral-English `.resx` catalogs behind a small
`Flowspan.Desktop` resource facade. Avalonia XAML resolves named application
resources populated at startup. Desktop view models resolve complete values and
format templates through the same facade. Formatting follows the current UI and
display cultures, while identifiers, wire values, reason codes, schema fields,
and diagnostic formats remain explicit invariant contracts.

The catalog fails verification on missing or blank keys. Source-contract tests
reject literal visible/accessibility text in XAML and presentation prose in
view models. v1 includes no culture-specific satellite catalog and no runtime
language switch.

## Consequences

- Adding a maintained language later uses the standard .NET satellite-resource
  model without changing domain or view-model interfaces.
- Neutral-English wording can change independently of executable control flow.
- Resource keys and format placeholders become tested API within the desktop
  presentation layer.
- The desktop assembly gains no third-party localization dependency.
- Protocol tokens and diagnostic schemas are not translated accidentally.
- Resource tests do not prove native screen-reader or high-contrast behavior;
  those gates remain separate.

## Alternatives considered

### Keep English literals in code until a translation is commissioned

This fails R10.5 and permits every new surface to increase later migration risk.

### Add a third-party localization framework

Runtime language switching and pluralization infrastructure are unnecessary for
the approved one-language v1 scope. The added dependency and lifecycle surface
would not improve current acceptance evidence.

### Store strings only in Avalonia resource dictionaries

This works for static XAML but makes headless view-model tests depend on an
Avalonia application lifetime and weakens the desktop outer-layer boundary.
`.resx` plus `ResourceManager` works from both XAML composition and plain C#.
