# 0003: Offer SkipNegotiation over WebSockets as the affinity-free topology, and spell out its costs

- Status: accepted
- Date: 2026-11-13

## Context

Sticky sessions exist for SignalR because a connection is two HTTP requests (negotiate, then connect) that must meet
on one server. If the client skips negotiation, the connection is a single WebSocket request: whichever server receives
it creates and owns the connection, and there is nothing to be sticky about. Microsoft's scaling document names this as
the one case where sticky sessions are not needed
([SignalR hosting and scaling](https://learn.microsoft.com/aspnet/core/signalr/scale)); platform guides that cannot
offer affinity recommend the same client setting
([Heroku: scaling ASP.NET Core SignalR](https://devcenter.heroku.com/articles/scaling-aspnetcore-signalr-apps)), and
reports from Kubernetes deployments describe the negotiate/connect split as the failure that bites first
([G-Research: SignalR on Kubernetes](https://www.gresearch.com/news/signalr-on-kubernetes/)).

A backplane is still needed for cross-server sends: skipping negotiation fixes routing, not fan-out (ADR 0001).

## Options considered

1. **Always require affinity.** The conservative default. Costs: a cookie-capable proxy, uneven load after scale-out
   (existing clients stay where they are), and every reconnect after a server loss lands on a random server anyway.
2. **SkipNegotiation + WebSockets + backplane (chosen as a measured alternative).**
3. **Azure SignalR Service.** Removes both problems by holding the connections outside the app. Out of scope: not
   self-hostable, and the lab is about what the application's own servers do.

## Decision

The lab measures `rr-skip` (round-robin, no affinity, `SkipNegotiation = true`, `Transports = WebSockets`, Redis
backplane) next to `sticky-redis`, with the same servers and the same proxy process.

The costs of choosing it in a real system, which the lab does not hide:

- **No transport fallback.** Without negotiate the client cannot fall back to Server-Sent Events or long polling. A
  client behind a proxy that blocks WebSockets cannot connect at all.
- **No negotiate-time features.** Negotiate is also where the server can redirect a client (Azure SignalR Service uses
  it) and where `negotiateVersion` and stateful-reconnect support are agreed. Stateful reconnect in particular needs
  the reconnecting client to reach the server that holds its buffer, which is affinity again.
- **Authentication moves to the WebSocket request.** Tokens go in the `access_token` query string on the upgrade
  request, which ends up in proxy access logs unless they are configured to drop it.

## Consequences

- The README recommends `rr-skip` only with these caveats, and `sticky-redis` as the default for browser clients on
  networks the operator does not control.
- The integration test `Skipping_negotiation_makes_affinity_unnecessary_and_the_backplane_reaches_everyone` and the
  compose demo keep the claim verified; `Round_robin_without_affinity_sends_the_websocket_to_the_server_that_did_not_negotiate`
  shows the failure it avoids, deterministically (with two servers and strict round-robin, every negotiate and its
  WebSocket go to different servers).
