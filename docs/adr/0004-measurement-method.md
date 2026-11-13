# 0004: Measure from one load-generator process, with a single clock, staged ramps and forced-GC memory snapshots

- Status: accepted
- Date: 2026-11-13

## Context

The brief asks for 10,000 concurrent connections, memory and latency graphs, and real numbers on a shared machine
(Docker Desktop VM with 16 CPUs and 7.6 GB, other workloads running). The numbers have to mean something:

- **Latency** of a broadcast is "from the moment it is sent to the moment a client has it". Across processes on
  different clocks that needs clock synchronisation, and the error would be the same order as the latency.
- **Memory per connection** is easy to get wrong: a .NET process's RSS includes garbage not yet collected, the GC's
  retained segments, JIT code and connection pools created on first use.
- **Connections** on one host are bounded by ephemeral ports (Linux default 32768-60999, about 28,000 per destination
  address and port), file descriptors (Docker Desktop's default soft limit here is 1,048,576) and memory.

## Options considered

1. **NBomber or k6 with a SignalR plugin.** Good at request/response load. A broadcast fan-out test needs every client's
   receive time for every message against the send time, which is custom code either way.
2. **Several load-generator containers.** Spreads client CPU, but receive times would come from different processes;
   containers share the VM's clock, so this is workable, but the bookkeeping (which client should have received what)
   then spans processes.
3. **One load-generator process (chosen),** sized to hold 10,000 `HubConnection`s (4 CPUs, 3 GB limit).

## Decision

- **One clock.** The load generator stamps `Stopwatch.GetTimestamp()` immediately before it asks hub-1 to broadcast
  (`POST /api/broadcast/{hub}`), and every client callback stamps its receive with the same monotonic clock.
  `FanoutLedger` records receives lock-free (one interlocked increment and one array write per receive). Latency is
  therefore "send request issued -> client callback ran", and includes the HTTP call to hub-1, the server-side send,
  Redis pub/sub when used, the proxy hop when used, and the client's own receive processing.
- **Staged ramp.** A run opens connections up to 1,000, 2,500, 5,000 and 10,000 attempts with 100 attempts in flight.
  At each plateau it waits 3 s, takes a memory snapshot, then sends 20 broadcasts at 2 per second (32-byte payload) and
  waits up to 5 s for stragglers. A broadcast's expected receivers are the clients connected when it was sent.
- **Memory.** `/api/stats?gc=true` runs two blocking, compacting full GCs before reading RSS and the GC heap size, on
  every hub server, the proxy and the load generator itself. Per-connection cost is `(RSS at stage - idle RSS) /
  connections`, summed over the hub servers. Before the idle snapshot each run warms up (60 connections, 3 broadcasts,
  closed), so JIT and first-use allocations are not charged to the first stage.
- **Fresh processes per run.** The matrix script restarts Redis, the hub servers and the proxy before every run, and
  interleaves scenarios inside each repetition. Three repetitions; every reported number is the median of the three
  per-run values (per-run percentiles first, then the median).
- **Fixed CPU and memory limits** per container (hubs 2 CPUs / 1 GB each, proxy 2 CPUs / 1 GB, Redis 1 CPU / 256 MB,
  load generator 4 CPUs / 3 GB), so a burst elsewhere on the host cannot hand one run more CPU than another.
- **CPU diagnostic.** Each stage records the average CPU cores every process used during the broadcast window, so a
  latency curve can be attributed to the side that saturated.
- **One shared negotiate pool in the client.** By default every `HubConnection` owns an `HttpClient` whose pooled
  negotiate connection idles for a minute: 10,000 extra sockets through the proxy, inflating proxy and server memory.
  The load generator routes all negotiate requests through one `SocketsHttpHandler` and keeps each client's cookies in
  its own `CookieContainer` (`CookieForwardingHandler`), which SignalR also hands to the WebSocket, so affinity still
  works.

## Consequences

- The latency numbers include the load generator's own receive cost. At 10,000 clients the client handles 10,000
  callbacks per broadcast; the CPU diagnostic in `benchmarks.md` shows how busy it was. The numbers are an upper bound
  on server-side fan-out latency, not a pure server measurement.
- All traffic stays inside one Docker bridge network on one VM. There is no real network latency or packet loss;
  absolute latencies are lower, and CPU contention higher, than in a real deployment.
- Forced full GCs make memory numbers reproducible; production processes carry more uncollected garbage than this.
- The idle baseline includes the server GC's per-heap reservations, so RSS per connection at 1,000 connections is
  noisier than at 10,000, where connection state dominates.
