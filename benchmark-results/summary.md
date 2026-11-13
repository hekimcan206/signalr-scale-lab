| Scenario | Attempted | Connected | Delivered | Latency p50 | Latency p99 | Last client p50 | Hub RSS total | Hub RSS / conn | Hub heap / conn | Proxy RSS / conn | Client RSS / conn | Runs |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| single | 1,000 | 1,000 | 100.0% | 7.0 ms | 30.0 ms | 12.9 ms | 319 MiB | 38.0 KiB | 22.6 KiB | no proxy | 84.1 KiB | 3 |
| single | 2,500 | 2,500 | 100.0% | 20.2 ms | 52.3 ms | 37.3 ms | 381 MiB | 39.9 KiB | 23.6 KiB | no proxy | 77.9 KiB | 3 |
| single | 5,000 | 5,000 | 100.0% | 37.1 ms | 109.6 ms | 72.1 ms | 431 MiB | 30.2 KiB | 23.9 KiB | no proxy | 68.1 KiB | 3 |
| single | 10,000 | 10,000 | 100.0% | 93.4 ms | 223.3 ms | 174.5 ms | 565 MiB | 28.8 KiB | 23.8 KiB | no proxy | 61.8 KiB | 3 |
| sticky-only | 1,000 | 1,000 | 33.4% | 3.8 ms | 13.7 ms | n/a (never complete) | 352 MiB | 46.9 KiB | 22.9 KiB | 43.7 KiB | 89.1 KiB | 3 |
| sticky-only | 2,500 | 2,500 | 33.4% | 8.9 ms | 26.9 ms | n/a (never complete) | 399 MiB | 41.1 KiB | 23.6 KiB | 41.4 KiB | 80.7 KiB | 3 |
| sticky-only | 5,000 | 5,000 | 33.3% | 15.4 ms | 36.0 ms | n/a (never complete) | 487 MiB | 36.9 KiB | 23.5 KiB | 37.0 KiB | 68.6 KiB | 3 |
| sticky-only | 10,000 | 10,000 | 33.3% | 32.1 ms | 87.9 ms | n/a (never complete) | 619 MiB | 32.9 KiB | 23.5 KiB | 35.4 KiB | 61.5 KiB | 3 |
| sticky-redis | 1,000 | 1,000 | 100.0% | 9.8 ms | 24.3 ms | 15.5 ms | 367 MiB | 60.9 KiB | 26.6 KiB | 43.8 KiB | 89.3 KiB | 3 |
| sticky-redis | 2,500 | 2,500 | 100.0% | 23.1 ms | 47.3 ms | 39.2 ms | 457 MiB | 60.7 KiB | 28.1 KiB | 43.6 KiB | 82.4 KiB | 3 |
| sticky-redis | 5,000 | 5,000 | 100.0% | 41.8 ms | 98.4 ms | 75.5 ms | 592 MiB | 58.0 KiB | 28.4 KiB | 39.4 KiB | 70.3 KiB | 3 |
| sticky-redis | 10,000 | 10,000 | 100.0% | 115.4 ms | 244.9 ms | 188.7 ms | 788 MiB | 49.1 KiB | 28.4 KiB | 37.6 KiB | 61.9 KiB | 3 |
| rr-negotiate | 1,000 | 333 | 100.0% | 4.7 ms | 10.4 ms | 6.5 ms | 352 MiB | 164.0 KiB | 33.5 KiB | 103.5 KiB | 202.0 KiB | 3 |
| rr-negotiate | 2,500 | 808 | 100.0% | 7.5 ms | 18.7 ms | 12.2 ms | 392 MiB | 117.5 KiB | 31.5 KiB | 85.6 KiB | 148.2 KiB | 3 |
| rr-negotiate | 5,000 | 1,629 | 100.0% | 17.1 ms | 36.9 ms | 27.0 ms | 449 MiB | 96.1 KiB | 31.2 KiB | 67.0 KiB | 121.5 KiB | 3 |
| rr-negotiate | 10,000 | 3,270 | 100.0% | 30.0 ms | 73.8 ms | 54.5 ms | 556 MiB | 79.8 KiB | 30.8 KiB | 53.0 KiB | 95.7 KiB | 3 |
| rr-skip | 1,000 | 1,000 | 100.0% | 9.9 ms | 25.9 ms | 16.4 ms | 362 MiB | 55.6 KiB | 25.9 KiB | 43.9 KiB | 77.0 KiB | 3 |
| rr-skip | 2,500 | 2,500 | 100.0% | 20.3 ms | 41.3 ms | 35.1 ms | 448 MiB | 57.6 KiB | 27.4 KiB | 36.2 KiB | 75.1 KiB | 3 |
| rr-skip | 5,000 | 5,000 | 100.0% | 45.8 ms | 168.2 ms | 92.3 ms | 582 MiB | 56.1 KiB | 27.7 KiB | 34.2 KiB | 66.2 KiB | 3 |
| rr-skip | 10,000 | 10,000 | 100.0% | 112.4 ms | 271.3 ms | 218.3 ms | 783 MiB | 48.6 KiB | 27.7 KiB | 32.8 KiB | 57.6 KiB | 3 |
