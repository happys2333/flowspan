# Desktop Quality and String Externalization Requirements

Status: approved baseline, implementation in progress

Parent requirements: R10.4-R10.5

## 1. Problem and scope

Flowspan's desktop shell already exposes the v1 workflows through Avalonia, but
its neutral-English presentation text is distributed across XAML and view-model
code. That makes later translation unsafe, lets new literals bypass review, and
makes culture-sensitive formatting inconsistent. The same slice must preserve
the established keyboard, non-color, scaling, contrast, and no-ambient-motion
contracts without claiming native accessibility evidence that headless tests
cannot provide.

This specification covers `Flowspan.Desktop` presentation text and deterministic
desktop-quality checks. It does not add a second maintained language, redesign
the interface, change protocol/domain values, or close physical-machine gates.

## 2. User stories

- As a desktop user, I can operate every core workflow by keyboard and receive
  named textual state rather than relying on color.
- As a user who increases text size or reduces motion, I can use the same
  controls without clipped critical state or required animation.
- As a future translator or maintainer, I can find user-visible desktop text in
  resource catalogs rather than searching executable UI code.
- As a release reviewer, I can distinguish deterministic headless evidence from
  Windows, macOS, and Linux native accessibility evidence.

## 3. Acceptance requirements

### DQ1 - Neutral-English resource boundary

1. When the desktop shell loads, Flowspan shall resolve visible labels,
   instructions, control content, window titles, tooltips, automation names,
   automation help text, and view-model presentation messages from embedded
   neutral-English resources.
2. When v1 is built without a culture-specific satellite resource, Flowspan
   shall use the neutral-English catalog for every UI culture.
3. When a catalog key is missing or blank during verification, the resource
   contract shall fail with the exact key instead of silently presenting the
   key or an empty value.
4. Product names, protocol names, capability identifiers, reason codes, JSON
   property names, file names, and other machine contracts shall retain their
   invariant representation. Human-readable text surrounding those values
   shall still come from a resource.

### DQ2 - Culture-aware presentation

1. When a localized template contains a displayed number, date, or time,
   Flowspan shall format that value with `CurrentCulture` unless the value is an
   explicitly invariant identifier, wire value, diagnostic value, or ISO-8601
   timestamp.
2. When user-controlled titles or device labels are inserted into a template,
   Flowspan shall pass them as format arguments and shall not construct a new
   resource key from user-controlled data.
3. When presentation text changes, operation, authorization, redaction, and
   fail-closed behavior shall remain unchanged.

### DQ3 - Regression gate

1. When a developer adds a user-visible literal to desktop XAML, the automated
   resource-boundary test shall fail.
2. When a developer adds presentation prose directly to a desktop view model,
   the automated resource-boundary test shall fail unless the value is an
   explicitly classified machine contract.
3. When a resource reference is added, the automated test shall require a
   non-empty neutral-English value and shall reject duplicate keys.
4. The gate shall cover visible and accessibility-only text; an automation name
   is not exempt merely because it is not painted.

### DQ4 - Deterministic accessibility checks

1. When a core command is enabled, it shall be reachable and activatable through
   standard keyboard interaction and expose a programmatic name and state.
2. When status changes, Flowspan shall expose an explicit textual state and
   shall not use color as the only carrier of meaning.
3. When the minimum supported window is exercised with increased text size,
   critical content shall wrap or scroll instead of overlapping or clipping,
   and interactive targets shall retain a minimum 44 device-independent-pixel
   height.
4. The safety palette shall continue to meet the documented contrast floors.
5. While the v1 interface contains no required motion, desktop XAML shall not
   introduce animation or transition resources. Any later required motion shall
   add a reduced-motion path before it is accepted.

### DQ5 - Evidence boundary

1. Headless tests may prove resource resolution, control-tree construction,
   keyboard routing, automation metadata, logical sizing, declared colors, and
   absence of configured animation.
2. Flowspan shall not treat headless tests as proof of native screen-reader
   speech/order, operating-system high-contrast behavior, visible focus rings,
   font fallback, native text scaling, or reduced-motion integration.
3. Before the desktop-quality release criterion is closed, Windows, macOS, and
   Linux real-machine evidence shall cover the native checks in DQ5.2.

## 4. Non-goals

- Shipping or maintaining a translation other than neutral English in v1.
- Runtime language switching.
- Translating diagnostic schemas, wire protocol values, reason codes, or
  capability identifiers.
- Changing layout hierarchy, visual direction, or product terminology.
- Claiming that headless Avalonia proves platform accessibility behavior.
