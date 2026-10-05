# Hosted regression reliability

Scope: repair the two independently observed failures in CI `37281400039`
for `65589d2418bdc32f2cb387c98cc3eeb5d3670b60`, without weakening product
deadlines, cancellation, authorization or cleanup assertions. Trace: R9, R12,
MEP8. This is not native or v1 acceptance.

- HRR1: When late renderer cleanup has either legitimate owner, the participant
  stop test shall observe the blocked renderer, keep external Stop pending,
  release its own barriers, and join preparation and cleanup without imposing a
  contradictory test-only wait order.
- HRR2: When successful real-loopback authentication is tested under scheduler
  delay, the inbound deadline shall use an explicitly controlled test clock;
  omitted clock injection shall retain System time and existing timeout values.
- HRR3: When the injected inbound deadline reaches exact equality before
  authentication completes, the real connection shall fail closed, report
  timeout, run no handler and release its timer/connection obligations. Caller
  cancellation shall remain distinguishable from deadline expiration.
- HRR4: Before the repair closes, original qualified test identities, actual
  failing evidence, focused regressions, frozen solution Debug/Release and fresh
  exact-SHA Windows/macOS/Linux CI/CodeQL shall be verified. A matching local
  symptom shall not be presented as proof of the original Windows trigger.

Non-goals: changing the wire protocol, extending handshake budgets, changing
production native availability, or masking failures through retries/Skip.
