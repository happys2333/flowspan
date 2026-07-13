# ADR 0004: DNS-SD discovery boundary and signed short-lived offers

- Status: Accepted; provisional browse/publish adapter implemented, physical
  validation open
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

### Follow-up adapter spike

The deferred adapter spike inspected the exact `Makaretu.Dns.Multicast` 0.27.0
source tag (`b9f2f8158052568a19d09536179ceaf5cae9b23e`) and package on
2026-07-13. It opens IPv4 and IPv6 multicast clients, advertises and browses
through `ServiceDiscovery`, and subscribes to .NET network-address changes. Its
interface reconciliation recreates sockets only when network-interface IDs
change, so an address change on the same interface is not a sufficient
production lifecycle guarantee by itself.

The package targets .NET Standard 2.0 and resolves on .NET 10, but was published
in 2019. Its locked graph includes old `Common.Logging`, `IPNetwork2`,
`Makaretu.Dns`, `SimpleBase`, and `Tmds.LibC` versions. NuGet reported no known
vulnerability on the query date, but several newer transitive versions exist.
The nupkg contains neither a license expression nor a license file; the exact
source tag contains an MIT license. This provenance gap remains part of release
license review.

Thin native adapters avoid the managed raw-DNS parser but create three distinct
lifecycles:

| Platform | Native surface | Cost/risk |
| --- | --- | --- |
| Windows | `DnsServiceBrowse`/`DnsServiceRegister` in `dnsapi.dll` | Windows 10 desktop API, callback/PInvoke ownership and cancellation |
| macOS | `DNSServiceBrowse`/`DNSServiceRegister` in `dns_sd.h` | daemon socket/dispatch integration and native callback ownership |
| Linux | Avahi client API | separate daemon availability, poll integration, and an explicit unavailable mode |

Sources include Microsoft Learn's `DnsServiceBrowse` and `DnsServiceRegister`
requirements, Apple's open-source `dns_sd.h`, and Avahi's client browser example.
The cross-platform managed adapter is smaller for the first browser slice; the
native option remains the replacement path if physical reliability or package
review fails.

## Decision

- Keep DNS-SD behind `IDnsSdServiceBrowser`, `IDnsSdServicePublisher`, and
  `IPeerConnectionCandidateSource`; isolate the third-party package in
  `Flowspan.Transport.Mdns` so the core and tests do not depend on its types.
- Use DNS-SD service type `_flowspan._tcp.local`.
- Advertise only a short-lived signed offer: device ID/name, identity
  fingerprint, protocol versions, TCP port, issue/expiry time, and random nonce.
  Never advertise Activity names, capabilities, trust state, or content.
- Discovery does not grant trust. A paired peer verifies the offer with its
  stored identity key; an unpaired peer verifies after the candidate connection
  presents its key, then completes SAS pairing.
- Use pinned `Makaretu.Dns.Multicast` 0.27.0 provisionally for the production
  browser. Recreate the entire package stack on every outer BCL address-change
  event instead of relying only on the package's interface-ID reconciliation.
- Treat every packet and TXT property as untrusted. A bounded resolution cache
  accepts at most 256 records per batch, 128 instances, 32 addresses per host,
  and 16 TXT properties. It produces candidates only after SRV, TXT, and A/AAAA
  data agree with the signed offer and the current trusted identity.
- Generate a new signed offer immediately and every 45 seconds with a 90-second
  lifetime. Refresh withdraws the old profile before advertising and announcing
  the new one, preferring a short absence over simultaneous stale and current
  offers. Cancellation withdraws the active profile; a network-stack rebuild
  replays only the most recent successfully accepted offer.
- Keep physical dual-stack/interface churn, sleep/wake, VPN, and two-device
  evidence open. A failed physical matrix or unresolved package
  provenance/maintenance review triggers replacement by native adapters.

## TXT/connection split

DNS-SD TXT records remain small and individually bounded. The implemented v1
form serializes the signed offer into a canonical payload of at most 768 bytes,
Base64-encodes it, and splits it into at most five `fsN` properties. Every full
`key=value` string is at most the DNS character-string limit of 255 bytes;
`txtvers=1` and a canonical chunk count make missing, duplicate, reordered-name,
and non-canonical payloads rejectable. The payload contains device ID/name,
fingerprint, signed port, protocol range, nonce, issue/expiry times, and
signature—never Activity content, capabilities, or trust state.

## Consequences

- The simulator can prove discovery expiry, tamper rejection, identity binding,
  deduplication, and reconnect timing without multicast access.
- Portable tests prove split SRV/TXT/address resolution, dual-stack candidate
  rotation, hostile TXT and record-count limits, same-key rename, removal/TTL,
  stack-restart cleanup, canonical profile publication, timed nonce refresh,
  withdraw, publish/restart replay, and injected bind/restart/announce/cleanup
  failures, including replacement progress after old-stack cleanup failure. They
  do not prove a packet crossed a physical interface.
- Real zero-config acceptance remains open until an adapter passes Windows,
  macOS, Linux, IPv4/IPv6, VPN/multiple-interface, sleep/wake, and network-change
  tests.
- macOS/iOS multicast entitlement notes in Zeroconf documentation are relevant
  to packaging research even though mobile is not v1 scope.
- The provisional package is locked and audited, but final license/provenance
  and maintenance acceptance remain open.
