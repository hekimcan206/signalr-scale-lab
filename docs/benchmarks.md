# Benchmarks

One question per measurement:

| | Question | Method |
|---|---|---|
| [Topology matrix](#topology-matrix) | What does each topology deliver, and what does it cost in memory and broadcast latency, from 1,000 to 10,000 connections? | 5 scenarios x 4 stages x 3 repetitions through the compose stack |
| [Backplane subscriptions](#backplane-subscriptions) | Why does the Redis backplane cost memory per connection? | `redis-cli pubsub channels` while 2,000 backplane connections are open |
| [Client CPU check](#client-cpu-check) | Is the load generator the bottleneck for latency at 10,000 clients? | Single-node 10,000-connection run with 4 vs 8 load-generator CPUs |

Every number below was measured on this machine. Raw outputs: [`benchmark-results/runs/`](../benchmark-results/runs)
(one JSON file per run, every stage with all processes' memory, fan-out summary, failure reasons and CPU use),
[`benchmark-results/summary.md`](../benchmark-results/summary.md) (generated from them),
[`benchmark-results/host-load.log`](../benchmark-results/host-load.log) and
[`benchmark-results/experiments/`](../benchmark-results/experiments).

## Environment

| | |
|---|---|
| Host | Windows 11 Home, 13th Gen Intel Core i7-13620H (10 cores, 16 threads), 15.7 GB RAM |
| Docker | Docker Desktop, engine 29.9.1, Linux VM with 16 CPUs and 7.6 GB RAM; ephemeral port range 32768-60999, `nofile` 200,000 per container |
| .NET | Runtime 10.0.15 in `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, Release builds, server GC (default for ASP.NET Core) |
| Packages | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.15, `Microsoft.AspNetCore.SignalR.Client` 10.0.15, `Yarp.ReverseProxy` 2.3.0 |
| Redis | `redis:7.4.11-alpine`, persistence off |
| Limits | 3 hub servers with 2 CPUs / 1 GB each, proxy 2 CPUs / 1 GB, Redis 1 CPU / 256 MB, load generator 4 CPUs / 3 GB |
| Host load | Shared, not idle: 11-12 containers running (another project's six database containers plus this stack). VM load average 0.5-5.5 at the start of the runs (recorded per run in `host-load.log`) |

All processes run in one VM on one Docker bridge network. There is no real network between them: no propagation
delay, no packet loss, and every process competes for the same physical CPUs.

## Topology matrix

```bash
bench/run-matrix.sh            # REPS=3, STAGES=1000,2500,5000,10000 by default; about 25 minutes
dotnet run -c Release --project src/ScaleLab.LoadGen -- report --env "$(cat benchmark-results/environment.txt)"
```

**Method** ([ADR 0004](adr/0004-measurement-method.md)). Before each run the script restarts Redis, the three hub
servers and the proxy. The load generator warms up (60 connections, 3 broadcasts, closed), takes an idle memory
snapshot, then ramps to 1,000, 2,500, 5,000 and 10,000 connection attempts (100 in flight). At each plateau: 3 s settle,
memory snapshot of every process after two forced compacting full GCs, then 20 broadcasts at 2 per second with a
32-byte payload, sent to hub-1's API, and up to 5 s for stragglers. Latency is measured from just before the broadcast
request to the client callback, on one monotonic clock in the load generator. Scenarios are interleaved within each of
the three repetitions. Every value below is the median of the three runs' values.

**Result: every scenario reached 10,000 concurrent connections**, in every repetition, with zero dropped connections
and zero unattributable receives. The machine did not have to be scaled down.

### What each topology delivers (10,000 attempts)

| Scenario | Connections open | Broadcast receives / connected clients | Why |
|---|---:|---:|---|
| `single` (one server, no proxy) | 10,000 | 100.0% | baseline |
| `sticky-only` (affinity, no backplane) | 10,000 | **33.3%** | only hub-1's clients (3,334 of 10,000) receive what hub-1 sends |
| `sticky-redis` (affinity + Redis) | 10,000 | 100.0% | the documented setup |
| `rr-negotiate` (no affinity, negotiate) | **3,270** | 100.0% | 6,730 attempts failed: `WebSockets failed: The server returned status code '404' when status code '101' was expected.` |
| `rr-skip` (no affinity, skip negotiate + Redis) | 10,000 | 100.0% | one request per connection, nothing to be sticky about |

The `rr-negotiate` success rate (32.6-33.6% across runs) is what chance predicts: with three servers, the WebSocket
request lands on the server that handled the negotiate one time in three. Connections that did open receive everything,
because the backplane is in place. Round-robin placement was exact: at 10,000 the three servers held 3,334 / 3,333 /
3,333 connections in the sticky scenarios.

![delivery](images/delivery.svg)

### Memory

| Scenario @ 10,000 | Hub servers RSS (sum of 3) | Hub RSS / connection | Hub managed heap / connection | Proxy RSS / connection | Client RSS / connection |
|---|---:|---:|---:|---:|---:|
| `single` | 565 MiB | 28.8 KiB | 23.8 KiB | no proxy | 61.8 KiB |
| `sticky-only` | 619 MiB | 32.9 KiB | 23.5 KiB | 35.4 KiB | 61.5 KiB |
| `sticky-redis` | 788 MiB | **49.1 KiB** | **28.4 KiB** | 37.6 KiB | 61.9 KiB |
| `rr-skip` | 783 MiB | **48.6 KiB** | **27.7 KiB** | 32.8 KiB | 57.6 KiB |

Idle, the three hub servers together use 277-310 MiB RSS (about 90-105 MiB each, of which only about 2.3 MiB is
managed heap: the rest is the runtime, JIT code and the server GC's reservations).

Readings:

- **A connection costs about 24 KiB of managed heap on the hub server** without the backplane (the connection context,
  its pipes and the WebSocket transport state), and about 29-33 KiB of RSS once heap fragmentation and native buffers
  are counted.
- **The Redis backplane adds about 4-5 KiB of managed heap and about 16 KiB of RSS per connection** on the hub servers
  (`sticky-redis` vs `sticky-only`: same routing, same proxy, only the lifetime manager differs). The cause is that
  `RedisHubLifetimeManager` subscribes one Redis channel per connection, see [below](#backplane-subscriptions).
- **Sticky or not makes no memory difference** (`sticky-redis` 49.1 KiB vs `rr-skip` 48.6 KiB on the hubs; the proxy
  holds 37.6 vs 32.8 KiB per connection, within run-to-run noise of a few KiB). The affinity cookie costs nothing
  measurable; the proxy itself does.
- **The proxy is not free**: every proxied WebSocket is two TCP connections and two sets of buffers in YARP, about
  33-38 KiB per connection, comparable to a hub server's cost. At 10,000 connections the proxy held 414-469 MiB RSS.
- Per-connection numbers at 1,000 connections are higher (38-61 KiB) because fixed costs that appear with the first
  connections (thread-pool growth, pool buffers) are divided by fewer connections. The 10,000 row is the steady state.
- `rr-negotiate`'s per-connection numbers (summary table) are inflated: 6,730 failed attempts still cost memory on
  their way through, and only 3,270 connections remain to divide by.

![hub server memory](images/memory-total.svg)
![memory per connection](images/memory-per-connection.svg)

### Broadcast latency

| Scenario | p50 @ 1,000 | p99 @ 1,000 | p50 @ 10,000 | p99 @ 10,000 | Last client p50 @ 10,000 |
|---|---:|---:|---:|---:|---:|
| `single` | 7.0 ms | 30.0 ms | 93.4 ms | 223.3 ms | 174.5 ms |
| `sticky-only` | 3.8 ms | 13.7 ms | 32.1 ms | 87.9 ms | never complete |
| `sticky-redis` | 9.8 ms | 24.3 ms | 115.4 ms | 244.9 ms | 188.7 ms |
| `rr-negotiate` | 4.7 ms (333 clients) | 10.4 ms | 30.0 ms (3,270 clients) | 73.8 ms | 54.5 ms |
| `rr-skip` | 9.9 ms | 25.9 ms | 112.4 ms | 271.3 ms | 218.3 ms |

"Last client" is the time until the last of the connected clients had the broadcast (the fan-out completion time).

Readings:

- **Latency grows with the number of clients that receive a broadcast, not with the number of servers.** One server
  pushing to 10,000 clients (`single`) and three servers pushing to 3,333 each through a proxy (`sticky-redis`) land in
  the same range (93 vs 115 ms p50). `sticky-only` and `rr-negotiate`, where about 3,300 clients receive, sit at
  30-32 ms, close to `single` at 2,500 clients (20 ms) and 5,000 clients (37 ms).
- **The backplane and proxy hop costs a few milliseconds at low load**: 7.0 ms (`single`) vs 9.8 ms (`sticky-redis`)
  p50 at 1,000 clients. At 10,000 the difference (93 vs 115 ms) is within the spread of single runs: `rr-skip`'s three
  10,000-connection runs gave 74.9, 136.8 and 112.4 ms p50.
- **Sticky vs skip-negotiate makes no latency difference** once connected: affinity only affects the connect path.
- Every 10,000-client broadcast in a topology passes through at least one process that handles all 10,000 frames:
  hub-1 in `single`, the proxy in the proxied scenarios, and the load generator in all of them. The average CPU use
  during the broadcast window at 10,000 (`sticky-redis`, run 2) was 0.30 cores per hub server, 0.92 cores for the proxy
  (of 2) and 1.38 cores for the load generator (of 4): averages over a 15-second window that is idle between bursts, so
  they show where work happens, not that any process was saturated.
- These are in-VM numbers: no real network, and the same physical CPUs serve clients, proxy and servers.

![latency p50](images/latency-p50.svg)
![latency p99](images/latency-p99.svg)

## Backplane subscriptions

Measured once with `redis-cli` during a 2,000-connection `sticky-redis` run
([raw](../benchmark-results/experiments/redis-channels.txt)):

| | Redis pub/sub channels |
|---|---:|
| Idle stack, 0 connections | 9 |
| 2,000 connections open | 2,009 |
| After the clients disconnected | 9 |

Channel names such as `scalelabScaleLab.HubServer.FanoutHub:connection:<connection id>` show that
`RedisHubLifetimeManager` subscribes one channel per connection, so that `Clients.Client(id)` works from any server.
Redis subscriptions, and each hub server's subscription bookkeeping, grow linearly with connections; this is the
extra 4-5 KiB of managed heap per connection measured above. Redis itself stayed small (1.32 MB `used_memory` after the
run). The channel prefix is concatenated without a separator because `ChannelPrefix` is used verbatim.

## Client CPU check

One extra pair of single-node runs at 10,000 connections with the load generator limited to 4 and to 8 CPUs
([raw](../benchmark-results/experiments)):

| Load-generator CPUs | p50 | p99 | Load generator CPU during broadcasts | hub-1 CPU |
|---:|---:|---:|---:|---:|
| 4 | 117.5 ms | 461.2 ms | 1.56 cores | 1.08 cores |
| 8 | 99.8 ms | 232.4 ms | 1.56 cores | 0.83 cores |

Doubling the client's CPUs did not change its CPU use and did not change latency beyond the run-to-run spread (the
matrix's three 4-CPU runs gave 88.8-96.7 ms p50; the 4-CPU run here was slower and had a 15.6 s ramp, a sign of host
contention at that moment). The load generator is therefore not CPU-bound at 10,000 clients. This is one pair of runs,
not a full matrix, and does not rule out other client-side limits (scheduling, the shared VM network stack).

## What was not measured

- Real networks: everything ran inside one VM. Expect lower CPU contention and higher absolute latency across hosts.
- More than 10,000 connections. It was not attempted: at 10,000 the proxy already held about 470 MiB of its 1 GB
  limit and the load generator about 700 MiB, and the Docker VM is shared with other workloads. Ephemeral ports would
  allow about 28,000 connections from one client container to one proxy address.
- Group and user sends, client-to-server message rates, reconnect storms after a server loss, Redis failover.
- Proxies other than YARP; transports other than WebSockets.
