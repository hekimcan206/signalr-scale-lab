# 0005: Test the topology on real sockets with a real Redis, and keep the published report reproducible

- Status: accepted
- Date: 2026-11-13

## Context

The repository's claims are behavioural ("without a backplane a broadcast reaches only one server's clients", "without
affinity a negotiated WebSocket fails") and numerical (the benchmark tables and charts). Both kinds can rot: a package
upgrade can change routing or lifetime-manager registration, and a hand-edited number can drift from the data.

## Options considered

1. **`WebApplicationFactory` / `TestServer`.** Fast, but the test server is in-memory: YARP forwarding to another
   in-memory server and WebSocket upgrades through the proxy are exactly the parts that would not be real.
2. **Mocked Redis.** Would test our code, not the backplane's behaviour.
3. **Real Kestrel on loopback, real Redis in Testcontainers (chosen),** plus pure unit, property and golden tests for
   everything that is not I/O.

## Decision

- **Integration tests** (`tests/ScaleLab.IntegrationTests`): one Redis 7.4.11 container, two hub servers and the YARP
  proxy started in-process with `HubServerApp.Build` / `ProxyApp.Build` on `127.0.0.1:0`. They assert each topology's
  behaviour, the lab key, input validation and a miniature bench run (two stages) end to end. Test classes run
  serially (`xunit.runner.json`), which keeps connection counts per server attributable.
- **Unit and property tests** (`tests/ScaleLab.UnitTests`, FsCheck): nearest-rank percentiles are observed values and
  monotonic in p; delivery ratio is capped and completion is defined only for complete broadcasts; axis ticks cover
  the range with round steps; chart labels keep their order and spacing; the proxy configuration gives affinity to the
  sticky cluster only.
- **Golden tests** for the markdown table and the five charts, generated from a fixed synthetic set of runs.
- **Report reproducibility in CI**: the committed `docs/images/*.svg` and `benchmark-results/summary.md` are regenerated
  from the committed run files and `git diff --exit-code` must be empty. A number in the README that is not in a run file
  cannot survive a CI run.
- **The compose demo in CI** runs all five scenarios at 300 connections and fails unless each behaves as expected, then
  runs a two-stage bench as a smoke test of the measurement path.

## Consequences

- The integration suite needs Docker (as CI's ubuntu runner has). It takes about 15 s locally.
- The full 10,000-connection matrix is not run in CI (about 25 minutes, and a shared runner's numbers would not be
  comparable). It is run by hand with `bench/run-matrix.sh`, and its raw output is committed.
