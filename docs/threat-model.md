# Threat model

signalr-scale-lab is a measurement lab, not a product, but its parts are the parts of a real scaled-out SignalR
deployment: hub servers, a shared Redis backplane and a reverse proxy with session affinity. This document lists what
an attacker could do to such a deployment, what the lab does about it, and what a production system would need on top.

## Scope and assets

| Asset | Why it matters |
|---|---|
| Broadcast channel (`/api/broadcast`, the hubs' `Clients.All`) | Whoever can send reaches every connected client: content injection, fan-out amplification |
| Redis backplane | Every server trusts what it reads from the backplane channels and delivers it to its clients |
| Affinity cookie (`.ScaleLab.Affinity`) | Decides which server a client's requests reach |
| Hub server and proxy capacity | Connections cost memory (measured: tens of KiB each, see benchmarks) and file descriptors |
| Stats endpoint (`/api/stats?gc=true`) | Forces blocking full GCs: a pause on demand |

Data: all payloads are synthetic (`"xxxx..."` strings and sequence numbers). There is no personal data anywhere in the
system, so confidentiality of message content is out of scope for the lab itself and listed under production notes.

## Actors

- **Anonymous network client** that can reach the proxy or a hub server.
- **Malicious connected client** that holds valid SignalR connections.
- **Neighbour on the Redis network** (another app sharing the Redis instance, or anything that can reach port 6379).

## Threats (STRIDE) and mitigations

| # | Threat | STRIDE | Lab mitigation | Production note |
|---|---|---|---|---|
| T1 | Anyone posts to `/api/broadcast` and pushes content to every client | Spoofing, Tampering | `/api` requires `X-Lab-Key`, compared in constant time (`CryptographicOperations.FixedTimeEquals`); missing or wrong key returns 401 (integration-tested). Compose binds published ports to 127.0.0.1 | Replace the shared key with real authentication and authorization on the send path; never expose an unauthenticated broadcast endpoint |
| T2 | Forcing `gc=true` repeatedly to pause the servers | Denial of service | Behind the same key; only the load generator calls it, between measurements | Remove the endpoint or restrict it to an admin network |
| T3 | Oversized broadcast payloads multiply by the client count (10,000 x payload per send) | Denial of service | Payload capped at `Lab:MaxPayloadBytes` (4,096 bytes), negative or larger sizes return 400 (integration-tested). SignalR's default `MaximumReceiveMessageSize` (32 KB) bounds client-to-server messages | Rate-limit senders; size limits per message type |
| T4 | Connection flood exhausts memory or file descriptors | Denial of service | Container memory limits (1 GB per hub, 1 GB proxy) contain the blast radius to one container; `ulimit nofile` is set explicitly | Kestrel `MaxConcurrentConnections` / `MaxConcurrentUpgradedConnections`, per-IP connection limits at the edge, authentication before upgrade |
| T5 | Writing to the backplane's Redis channels injects messages that every server delivers | Tampering, Elevation | Redis is reachable only on the compose network (no published port). Channels are prefixed (`Lab:ChannelPrefix`) so apps sharing a Redis do not cross-deliver by accident | Redis ACL user limited to the prefix's channels (`&scalelab*`), TLS, password, network policy; a prefix is isolation from mistakes, not from attackers |
| T6 | Reading the backplane sees every message to every client | Information disclosure | No real data in the lab | TLS to Redis; treat the backplane as carrying everything the hubs send |
| T7 | Forged affinity cookie steers a client to a chosen server | Tampering | `HashCookie` maps to a destination id hash; a forged value can only pick one of the configured servers, and SignalR still checks the connection token, so this cannot reach another client's connection. Cookie is HttpOnly, SameSite=Lax | Same; add `Secure` when the proxy terminates TLS (`SecurePolicy`) |
| T8 | Hot-spotting by sticky clients after a scale-out or server loss | Denial of service | Measured and documented, not mitigated: existing connections stay on their server; `Redistribute` re-balances clients whose server disappeared | Connection draining and client reconnect with jitter; or the affinity-free `rr-skip` topology (ADR 0003) |
| T9 | Redis outage silently stops cross-server delivery | Denial of service, Repudiation | The backplane does not buffer: sends during an outage are lost for remote clients. The lab measures delivery ratio, which would show it | Alert on backplane connection state; design messages to be re-fetchable (the backplane is not a durable bus) |
| T10 | Access tokens in WebSocket query strings end up in proxy logs | Information disclosure | No authentication in the lab; request logging is off | Strip `access_token` from proxy and server logs when using `SkipNegotiation` or WebSocket auth |
| T11 | Vulnerable dependency | Tampering | Pinned versions with central package management; CI fails on any known-vulnerable direct or transitive package; CodeQL (`security-and-quality`) on C# and workflow files. The backplane package was moved from 10.0.14 to 10.0.15 because restore flagged GHSA-wr8v-9x5q-ggcm | Same, plus image scanning |
| T12 | Container escape or lateral movement from a compromised process | Elevation | Chiseled .NET images: no shell, no package manager, non-root user (UID 1654), verified in CI | Read-only root filesystem, dropped capabilities, seccomp profile |

## Out of scope

- Client authentication and per-user authorization (the hubs accept anonymous connections by design of the lab).
- TLS termination (all traffic is on a local Docker network).
- Azure SignalR Service and other hosted alternatives.
