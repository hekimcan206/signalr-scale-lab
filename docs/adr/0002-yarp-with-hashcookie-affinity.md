# 0002: YARP as the load balancer, with HashCookie session affinity

- Status: accepted
- Date: 2026-11-13

## Context

The sticky-session scenarios need a reverse proxy that balances SignalR's negotiate request, upgrades WebSockets and
can pin a client to the server that negotiated it. The same proxy must also offer a route *without* affinity, so the
two can be compared through one process.

## Options considered

1. **nginx with `ip_hash` or `hash $cookie_...`.** The most common production answer. `ip_hash` is useless here: every
   load-generator connection comes from one container IP, so all ten thousand would land on one server and the test
   would measure a single node. A cookie hash needs a cookie the application sets, and nginx's built-in `sticky` cookie
   directive is part of the commercial NGINX Plus.
2. **HAProxy with `cookie SERVERID insert`.** Does exactly the right thing and is a fine production choice, but adds a
   second configuration language and an image the CI would have to pull; per-connection memory of a C proxy would also
   not be comparable with anything else in the repository.
3. **YARP (`Yarp.ReverseProxy` 2.3.0) (chosen).** A .NET reverse proxy configured in code. Session affinity is a
   per-cluster setting: when it is enabled, the first response carries an affinity cookie and later requests with that
   cookie go to the same destination
   ([YARP session affinity](https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/session-affinity)).
   Since YARP 2.0 the default policy is `HashCookie`, which stores a hash of the destination id instead of a
   Data-Protection-encrypted value, so several proxy instances agree on it without sharing keys
   ([v2.0 release notes](https://newreleases.io/project/github/microsoft/reverse-proxy/release/v2.0.0-rc.1)).

## Decision

YARP, built from `ProxyTopology` in code:

- Two routes, `/sticky/{**catch-all}` and `/rr/{**catch-all}`, each removing its prefix before forwarding.
- Two clusters with the same three destinations and `RoundRobin` load balancing. Only `sticky` enables affinity:
  policy `HashCookie`, cookie `.ScaleLab.Affinity` (HttpOnly, SameSite=Lax), failure policy `Redistribute` (a client
  whose server disappeared is re-balanced and SignalR fails its stale connection id, instead of a 503 for as long as the
  cookie lives).
- `ActivityTimeout` of two minutes. YARP applies it to WebSockets as idle time; SignalR's 15-second keep-alive keeps
  healthy connections well inside it.
- Destinations are validated at startup (absolute `http(s)` URLs), so a typo fails fast instead of producing 502s.

The affinity cookie only works if the client sends it on the WebSocket request. The .NET SignalR client passes the
`CookieContainer` from `HttpConnectionOptions.Cookies` to both the negotiate `HttpClient` and the `ClientWebSocket`;
the load generator's shared connection pool preserves that (see ADR 0004). The integration test
`Sticky_route_connects_every_negotiating_client_and_spreads_them_over_both_servers` proves it end to end.

## Consequences

- The proxy is a .NET process, so its per-connection memory is measured the same way as the hubs' (`/api/stats` after a
  forced full GC) and reported separately.
- Every proxied WebSocket is two TCP connections at the proxy (client side and server side). The proxy's memory per
  connection is part of the real cost of the sticky and round-robin topologies and is reported, not hidden.
- A browser client behaves the same as the .NET client here (the browser sends cookies on the WebSocket handshake to
  the same origin). A non-browser client that ignores cookies would break affinity silently; `rr-skip` is the
  topology that does not depend on cookies at all.
- Results from a C proxy (nginx, HAProxy, Envoy) would differ in proxy memory and latency. That is listed under
  Limitations in the README.
