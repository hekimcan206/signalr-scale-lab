| Scenario | Attempted | Connected | Delivered | Latency p50 | Latency p99 | Last client p50 | Hub RSS total | Hub RSS / conn | Hub heap / conn | Proxy RSS / conn | Client RSS / conn | Runs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| single | 1,000 | 1,000 | 100.0% | 3.0 ms | 9.0 ms | 6.0 ms | 115 MiB | 15.4 KiB | 7.7 KiB | no proxy | 15.4 KiB | 3 |
| single | 2,000 | 2,000 | 100.0% | 6.0 ms | 18.0 ms | 12.0 ms | 130 MiB | 15.4 KiB | 7.7 KiB | no proxy | 15.4 KiB | 3 |
| sticky-only | 1,000 | 1,000 | 33.4% | 3.3 ms | 9.9 ms | n/a (never complete) | 115 MiB | 15.4 KiB | 7.7 KiB | 1.5 KiB | 15.4 KiB | 3 |
| sticky-only | 2,000 | 2,000 | 33.4% | 6.6 ms | 19.8 ms | n/a (never complete) | 130 MiB | 15.4 KiB | 7.7 KiB | 1.5 KiB | 15.4 KiB | 3 |
| sticky-redis | 1,000 | 1,000 | 100.0% | 3.0 ms | 9.0 ms | 6.0 ms | 115 MiB | 15.4 KiB | 7.7 KiB | 1.5 KiB | 15.4 KiB | 3 |
| sticky-redis | 2,000 | 2,000 | 100.0% | 6.0 ms | 18.0 ms | 12.0 ms | 130 MiB | 15.4 KiB | 7.7 KiB | 1.5 KiB | 15.4 KiB | 3 |
| rr-negotiate | 1,000 | 340 | 100.0% | 3.0 ms | 9.0 ms | 6.0 ms | 115 MiB | 45.2 KiB | 22.6 KiB | 4.5 KiB | 45.2 KiB | 3 |
| rr-negotiate | 2,000 | 680 | 100.0% | 6.0 ms | 18.0 ms | 12.0 ms | 130 MiB | 45.2 KiB | 22.6 KiB | 4.5 KiB | 45.2 KiB | 3 |
| rr-skip | 1,000 | 1,000 | 100.0% | 3.2 ms | 9.5 ms | 6.0 ms | 115 MiB | 15.4 KiB | 7.7 KiB | 1.5 KiB | 15.4 KiB | 3 |
| rr-skip | 2,000 | 2,000 | 100.0% | 6.3 ms | 18.9 ms | 12.0 ms | 130 MiB | 15.4 KiB | 7.7 KiB | 1.5 KiB | 15.4 KiB | 3 |
