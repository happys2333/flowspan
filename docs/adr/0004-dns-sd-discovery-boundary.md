# ADR 0004: DNS-SD discovery boundary and signed short-lived offers

- Status: Accepted boundary; production library/adapter selection deferred
- Date: 2026-07-13

## Context

Flowspan needs zero-configuration LAN discovery without publishing Activity
content. Discovery is untrusted input and cannot itself establish pairing or
authorization. The implementation must advertise and browse on Windows, macOS,
and Linux, survive interface changes, and avoid coupling the domain to one mDNS
library.

## Evidence gathered

Package and repository metadata were queried on 2026-07-13:

| Candidate | Latest NuGet | Published | License | Advertise | Repository last push |
| --- | --- | --- | --- | --- | --- |
| `Makaretu.Dns.Multicast` | 0.27.0 | 2019-11-05 | MIT repository | yes, `ServiceDiscovery.Advertise` | 2024-04-24 |
| `Tmds.MDns` | 0.8.0 | 2023-04-21 | LGPL-2.1 repository | discovery-focused | 2024-04-18 |
| `Zeroconf` | 3.7.16 | 2024-12-17 | MIT | browse/resolve-focused | 2025-10-23 |

Sources:

- NuGet v3 registration/flat-container metadata for the three package IDs;
- <https://github.com/richardschneider/net-mdns>;
- <https://github.com/tmds/Tmds.MDns>;
- <https://github.com/novotnyllc/Zeroconf>.

Makaretu is the only evaluated candidate with an obvious portable advertise and
browse API, but its published package age is a maintenance/supply-chain risk.
Combining separate browse and native advertise implementations would increase
complexity. Writing an RFC 6762/6763 stack is out of scope and security-prone.

## Decision

- Define production discovery behind `IPeerDiscovery`/adapter boundaries in the
  transport layer.
- Use DNS-SD service type `_flowspan._tcp.local`.
- Advertise only a short-lived signed offer: device ID/name, identity
  fingerprint, protocol versions, TCP port, issue/expiry time, and random nonce.
  Never advertise Activity names, capabilities, trust state, or content.
- Discovery does not grant trust. A paired peer verifies the offer with its
  stored identity key; an unpaired peer verifies after the candidate connection
  presents its key, then completes SAS pairing.
- Implement and test canonical offer/signature/deduplication/expiry behavior now
  with an in-memory directory.
- Do not add an mDNS NuGet package until a network-interface churn and dual-stack
  spike compares Makaretu against thin native DNS-SD adapters on all three OSes.

## TXT/connection split

DNS-SD TXT records should remain small. The record carries a format version,
device ID, fingerprint, protocol range, nonce, expiry, and signature. If the
full signed offer exceeds the tested packet budget, TXT carries an offer digest
and token; the complete offer is fetched over the advertised TCP candidate
channel before pairing/authentication. Either form is covered by the same
signature and never trusted without identity verification.

## Consequences

- The simulator can prove discovery expiry, tamper rejection, identity binding,
  deduplication, and reconnect timing without multicast access.
- Real zero-config acceptance remains open until an adapter passes Windows,
  macOS, Linux, IPv4/IPv6, VPN/multiple-interface, sleep/wake, and network-change
  tests.
- macOS/iOS multicast entitlement notes in Zeroconf documentation are relevant
  to packaging research even though mobile is not v1 scope.
- The selected library, if any, needs a dependency/license/vulnerability ADR
  update and locked version.
