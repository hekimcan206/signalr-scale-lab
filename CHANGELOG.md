# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.0] - 2026-11-13

### Added

- Hub server with two hubs side by side: `/hubs/fanout` on the Redis backplane
  (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`, prefixed channels) and `/hubs/local` on the in-memory lifetime
  manager. Key-protected broadcast and stats API, per-hub connection gauge, health check, structured JSON logs and
  optional OpenTelemetry metrics over OTLP.
- YARP proxy with a sticky route (round-robin with HashCookie session affinity) and a plain round-robin route to the
  same servers, WebSocket counting and a stats endpoint.
- Load generator: thousands of SignalR clients in staged ramps through a shared negotiate pool that keeps each client's
  affinity cookie, lock-free broadcast accounting on one monotonic clock, forced-GC memory snapshots of every process,
  CPU use per process during the broadcast window.
- Five scenarios (single node, sticky without backplane, sticky with Redis, round-robin with negotiate, round-robin
  with skipped negotiation) and a demo that checks each topology's behaviour.
- `docker compose up` demo: Redis, three hub servers, the proxy and the load generator.
- Benchmark matrix script, raw results of 3 repetitions up to 10,000 connections, report builder that renders the
  summary table and SVG charts from the run files.
- Unit, FsCheck property, golden and Testcontainers integration tests (Redis 7.4.11, real Kestrel sockets).
- ADRs 0001-0005, threat model, benchmark report.
- CI: build, NuGet vulnerability audit, format check, tests with coverage, report reproducibility check, container
  images, compose demo with a bench smoke run; CodeQL.

### Security

- `Microsoft.AspNetCore.SignalR.StackExchangeRedis` pinned to 10.0.15 (10.0.14 is flagged by GHSA-wr8v-9x5q-ggcm);
  `Microsoft.AspNetCore.SignalR.Client` moved to 10.0.15 with it to keep the SignalR packages on one version.

[Unreleased]: https://github.com/hekimcan206/signalr-scale-lab/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/hekimcan206/signalr-scale-lab/releases/tag/v1.0.0
