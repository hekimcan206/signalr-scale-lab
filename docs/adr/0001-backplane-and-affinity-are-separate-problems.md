# 0001: Treat the backplane and session affinity as two separate problems, and measure both on one deployment

- Status: accepted
- Date: 2026-11-13

## Context

"How do I scale SignalR horizontally?" is usually answered with "add a Redis backplane" or with "turn on sticky
sessions", as if they were alternatives. They are not: they fix two different failures.

1. **Routing a connection.** A SignalR connection starts with an HTTP `POST /negotiate`, which returns a connection
   token; the WebSocket (or SSE / long-polling) request that follows must present that token to the *same* server,
   because only that server holds the connection's state. Behind a load balancer that balances each request on its
   own, the second request often lands elsewhere and fails with 404. Microsoft's scaling guidance says sticky sessions
   are required for this reason, except when every client uses WebSockets with `SkipNegotiation` (or when Azure SignalR
   Service holds the connections) ([SignalR hosting and scaling](https://learn.microsoft.com/aspnet/core/signalr/scale),
   [Redis backplane](https://learn.microsoft.com/aspnet/core/signalr/redis-backplane)).
2. **Reaching clients on other servers.** `Clients.All.SendAsync` on server A writes to the connections server A holds.
   Clients on B and C never see it. The backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) publishes every
   send to Redis pub/sub so that each server delivers it to its own connections. Affinity does nothing for this.

The lab has to show both failures and both fixes with numbers, and the comparison is only fair if everything else
(servers, network path, proxy process, client) stays the same.

## Options considered

1. **One compose stack per topology** (no proxy; proxy + sticky; proxy + sticky + Redis; ...), torn down and rebuilt
   between measurements. Simple to reason about, but every variant has a different process layout, and five stacks
   multiply the configuration that can drift.
2. **Restart the same stack with different environment variables** (`BACKPLANE=on`, `AFFINITY=off`). One file, but the
   variable under test is a restart away from the measurement, and the demo could not show all behaviours in one run.
3. **One deployment that exposes every topology at once (chosen).** Each hub server hosts two hubs:
   `/hubs/fanout` with the Redis lifetime manager and `/hubs/local` with the default in-memory one. The proxy exposes
   `/sticky/...` (round-robin with cookie affinity) and `/rr/...` (round-robin, no affinity) to the same three servers.
   A scenario is a choice of route, hub and negotiate mode.

## Decision

Option 3. `AddStackExchangeRedis()` registers `RedisHubLifetimeManager<>` as an open generic for every hub. A closed
registration takes precedence over an open generic one in `Microsoft.Extensions.DependencyInjection`, so

```csharp
services.AddSingleton<HubLifetimeManager<LocalHub>, DefaultHubLifetimeManager<LocalHub>>();
```

keeps `LocalHub` in-memory on a server that otherwise uses the backplane. The integration test
`In_memory_hub_on_the_same_servers_reaches_only_the_sending_servers_clients` pins that behaviour, because the whole
comparison depends on it.

The five scenarios (`ScenarioCatalog`):

| Scenario | Route | Hub | Negotiate | Expected |
|---|---|---|---|---|
| `single` | hub-1 directly | local | yes | baseline |
| `sticky-only` | `/sticky` | local | yes | every connection works, a broadcast reaches ~1/3 of clients |
| `sticky-redis` | `/sticky` | fanout | yes | everything works (the documented setup) |
| `rr-negotiate` | `/rr` | fanout | yes | ~2/3 of connections fail with 404 |
| `rr-skip` | `/rr` | fanout | skipped, WebSockets only | everything works without affinity |

Broadcasts are always sent to hub-1 through its HTTP API, so "which clients receive" depends only on the topology.

## Consequences

- One `docker compose up` demonstrates all five behaviours, and the demo asserts each one (CI runs it).
- The memory and latency comparison between sticky and non-sticky routing uses identical processes.
- Both hubs share each server's process, so idle memory includes both hubs' (small) static cost. Only one hub carries
  connections in any given run.
- Group and user sends (`Clients.Group`, `Clients.User`) go through the same lifetime manager and are not measured
  separately; the lab measures `Clients.All`, the worst case for fan-out.
