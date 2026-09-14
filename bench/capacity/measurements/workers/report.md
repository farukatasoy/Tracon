# Capacity report — 20260914-211707-workers

Profile: `workers`.
9 cell(s): 9 complete, 0 incomplete, 0 invalid.

## Environment

| Field | Value |
|---|---|
| Commit | df45a7bac76eb09997159e0a8372cf4ea826b007 |
| Tracon package | 0.0.0-capacity166.df45a7b |
| Operating system | Darwin 25.6.0 |
| Architecture | arm64 |
| Logical processors | 10 |
| Physical memory | 16 GiB |
| .NET runtime | 10.0.100 |
| PostgreSQL | 18.4 (Debian 18.4-1.pgdg12+1) |
| Database image | pgvector/pgvector:pg18 |

## Load points

| Scenario | Seed | Workers | Rate/s | Concurrency | Repeats | n | p50 ms | p95 ms | p99 ms | Throughput/s | Bytes/run | Rows/run |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| queued | empty | 1 | — | 8 | 3 | 1104 | 1320.39 | 1342.33 | 1368.94 ⚠repeat | 5.925 | 9.46 KiB (1 rpt) | 10.58 |
| queued | empty | 2 | — | 8 | 3 | 1108 | 1313.36 | 1343.26 | 1381.94 ⚠repeat | 5.929 | — (vacuum) | 10.65 |
| queued | empty | 4 | — | 8 | 3 | 1119 | 1305.96 | 1320 | 1332.34 ⚠repeat | 5.96 | — (vacuum) | 10.66 |

⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000). ⚠repeat marks one where the MERGED count clears the floor but the thinnest single repeat does not — the number is honest, but no individual window supported it. Percentiles are nearest rank over every repeat's samples merged together, never an average of the repeats' own percentiles. A bytes-per-run cell says how many repeats it came from when that is fewer than the repeat count; `1 rpt` is one window, not an average.

## What became of every request

`planned = not sent + sent` and `sent = accepted + rejected + failed + timed out` both close on every row; a report that cannot close them is hiding dropped load. A refusal or a timeout is a saturation finding and stays inside `sent` - it never leaves the distribution.

| Cell | Rate/s | Planned | Sent | Not sent | Accepted | Rejected | Timed out | Completed | Drain s | Queue wait p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-queued-w1-r1 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.06 | 117.49 |
| 001-queued-w1-r2 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.04 | 114.32 |
| 002-queued-w1-r3 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.07 | 113.54 |
| 003-queued-w2-r1 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.07 | 91.67 |
| 004-queued-w2-r2 | — | 372 | 372 | 0 | 372 | 0 | 0 | 372 | 0.07 | 77.43 |
| 005-queued-w2-r3 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.06 | 78.17 |
| 006-queued-w4-r1 | — | 369 | 369 | 0 | 369 | 0 | 0 | 369 | 0.07 | 36.76 |
| 007-queued-w4-r2 | — | 376 | 376 | 0 | 376 | 0 | 0 | 376 | 0.07 | 33.87 |
| 008-queued-w4-r3 | — | 374 | 374 | 0 | 374 | 0 | 0 | 374 | 0.06 | 35.67 |

## Reconciliation and resources

A store that refuses a write leaves the run green, so counting HTTP successes alone could report a clean measurement over lost data. Every cell therefore compares what was sent against what the store holds.

| Cell | Accepted runs | Terminal | Missing | Content mismatch | Sequence gaps | Live subs | Tenant bleed | Cross-tenant refused | Host peak RSS | Host CPU s |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-queued-w1-r1 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 225.98 MiB | 3.8 |
| 001-queued-w1-r2 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 243.8 MiB | 3.8 |
| 002-queued-w1-r3 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 240.25 MiB | 3.8 |
| 003-queued-w2-r1 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 240.98 MiB | 3.7 |
| 004-queued-w2-r2 | 372 | 372 | 0 | 0 | 0 | 372 | 0 | 2 | 242.83 MiB | 4.2 |
| 005-queued-w2-r3 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 236.83 MiB | 3.7 |
| 006-queued-w4-r1 | 369 | 369 | 0 | 0 | 0 | 369 | 0 | 2 | 243.59 MiB | 3.5 |
| 007-queued-w4-r2 | 376 | 376 | 0 | 0 | 0 | 376 | 0 | 2 | 238.81 MiB | 4 |
| 008-queued-w4-r3 | 374 | 374 | 0 | 0 | 0 | 374 | 0 | 2 | 244.09 MiB | 3.7 |

## Write amplification

Rows and bytes together, table by table, with index and TOAST split out from the heap.

| Cell | Table | Rows | Heap | Index | TOAST | Total | Autovacuum |
|---|---|---|---|---|---|---|---|
| 000-queued-w1-r1 | `runs` | 368 | 160 KiB | 296 KiB | 0 B | 456 KiB | — |
| 000-queued-w1-r1 | `run_events` | 2208 | 1.1 MiB | 88 KiB | 0 B | 1.19 MiB | — |
| 000-queued-w1-r1 | `run_inputs` | 368 | 432 KiB | 40 KiB | 0 B | 472 KiB | — |
| 000-queued-w1-r1 | `tool_invocations` | 368 | 480 KiB | 72 KiB | 0 B | 552 KiB | — |
| 000-queued-w1-r1 | `jobs` | 368 | 544 KiB | 144 KiB | 0 B | 688 KiB | — |
| 000-queued-w1-r1 | `traces` | 31 | 0 B | 0 B | 0 B | 0 B | — |
| 000-queued-w1-r1 | `spans` | 155 | 96 KiB | 0 B | 0 B | 96 KiB | — |
| 001-queued-w1-r2 | `runs` | 368 | 80 KiB | 312 KiB | 0 B | 392 KiB | ran |
| 001-queued-w1-r2 | `run_events` | 2208 | 1.12 MiB | 88 KiB | 0 B | 1.2 MiB | — |
| 001-queued-w1-r2 | `run_inputs` | 368 | 416 KiB | 40 KiB | 0 B | 456 KiB | — |
| 001-queued-w1-r2 | `tool_invocations` | 368 | 496 KiB | 72 KiB | 0 B | 568 KiB | — |
| 001-queued-w1-r2 | `jobs` | 368 | 568 KiB | 128 KiB | 0 B | 696 KiB | ran |
| 001-queued-w1-r2 | `traces` | 45 | 0 B | 0 B | 0 B | 0 B | — |
| 001-queued-w1-r2 | `spans` | 225 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |
| 002-queued-w1-r3 | `runs` | 368 | 88 KiB | 288 KiB | 0 B | 376 KiB | ran |
| 002-queued-w1-r3 | `run_events` | 2208 | 1.16 MiB | 88 KiB | 0 B | 1.24 MiB | — |
| 002-queued-w1-r3 | `run_inputs` | 368 | 360 KiB | 40 KiB | 0 B | 400 KiB | — |
| 002-queued-w1-r3 | `tool_invocations` | 368 | 504 KiB | 72 KiB | 0 B | 576 KiB | — |
| 002-queued-w1-r3 | `jobs` | 368 | 544 KiB | 136 KiB | 0 B | 680 KiB | ran |
| 002-queued-w1-r3 | `traces` | 30 | 0 B | 0 B | 0 B | 0 B | — |
| 002-queued-w1-r3 | `spans` | 150 | 88 KiB | 0 B | 0 B | 88 KiB | — |
| 003-queued-w2-r1 | `runs` | 368 | 8 KiB | 304 KiB | 0 B | 312 KiB | ran |
| 003-queued-w2-r1 | `run_events` | 2208 | 1.15 MiB | 88 KiB | 0 B | 1.23 MiB | — |
| 003-queued-w2-r1 | `run_inputs` | 368 | 384 KiB | 40 KiB | 0 B | 424 KiB | — |
| 003-queued-w2-r1 | `tool_invocations` | 368 | 504 KiB | 72 KiB | 0 B | 576 KiB | — |
| 003-queued-w2-r1 | `jobs` | 368 | 576 KiB | 152 KiB | 0 B | 728 KiB | ran |
| 003-queued-w2-r1 | `traces` | 45 | 0 B | 0 B | 0 B | 0 B | — |
| 003-queued-w2-r1 | `spans` | 225 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 004-queued-w2-r2 | `runs` | 372 | 136 KiB | 288 KiB | 0 B | 424 KiB | ran |
| 004-queued-w2-r2 | `run_events` | 2232 | 1.16 MiB | 88 KiB | 0 B | 1.25 MiB | — |
| 004-queued-w2-r2 | `run_inputs` | 372 | 472 KiB | 40 KiB | 0 B | 512 KiB | — |
| 004-queued-w2-r2 | `tool_invocations` | 372 | 528 KiB | 72 KiB | 0 B | 600 KiB | — |
| 004-queued-w2-r2 | `jobs` | 372 | 608 KiB | 144 KiB | 0 B | 752 KiB | ran |
| 004-queued-w2-r2 | `traces` | 44 | 0 B | 0 B | 0 B | 0 B | — |
| 004-queued-w2-r2 | `spans` | 220 | 128 KiB | 16 KiB | 0 B | 144 KiB | — |
| 005-queued-w2-r3 | `runs` | 368 | 200 KiB | 296 KiB | 0 B | 496 KiB | ran |
| 005-queued-w2-r3 | `run_events` | 2208 | 1.14 MiB | 88 KiB | 0 B | 1.23 MiB | — |
| 005-queued-w2-r3 | `run_inputs` | 368 | 464 KiB | 40 KiB | 0 B | 504 KiB | — |
| 005-queued-w2-r3 | `tool_invocations` | 368 | 528 KiB | 72 KiB | 0 B | 600 KiB | — |
| 005-queued-w2-r3 | `jobs` | 368 | 584 KiB | 144 KiB | 0 B | 728 KiB | ran |
| 005-queued-w2-r3 | `traces` | 32 | 0 B | 0 B | 0 B | 0 B | — |
| 005-queued-w2-r3 | `spans` | 160 | 104 KiB | 0 B | 0 B | 104 KiB | — |
| 006-queued-w4-r1 | `runs` | 369 | 80 KiB | 312 KiB | 0 B | 392 KiB | ran |
| 006-queued-w4-r1 | `run_events` | 2214 | 1.2 MiB | 88 KiB | 0 B | 1.28 MiB | — |
| 006-queued-w4-r1 | `run_inputs` | 369 | 560 KiB | 40 KiB | 0 B | 600 KiB | — |
| 006-queued-w4-r1 | `tool_invocations` | 369 | 680 KiB | 72 KiB | 0 B | 752 KiB | — |
| 006-queued-w4-r1 | `jobs` | 369 | 624 KiB | 136 KiB | 0 B | 760 KiB | ran |
| 006-queued-w4-r1 | `traces` | 48 | 0 B | 0 B | 0 B | 0 B | — |
| 006-queued-w4-r1 | `spans` | 240 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 007-queued-w4-r2 | `runs` | 376 | 112 KiB | 328 KiB | 0 B | 440 KiB | ran |
| 007-queued-w4-r2 | `run_events` | 2256 | 1.23 MiB | 88 KiB | 0 B | 1.31 MiB | ran |
| 007-queued-w4-r2 | `run_inputs` | 376 | 544 KiB | 40 KiB | 0 B | 584 KiB | — |
| 007-queued-w4-r2 | `tool_invocations` | 376 | 616 KiB | 72 KiB | 0 B | 688 KiB | — |
| 007-queued-w4-r2 | `jobs` | 376 | 600 KiB | 136 KiB | 0 B | 736 KiB | ran |
| 007-queued-w4-r2 | `traces` | 36 | 0 B | 0 B | 0 B | 0 B | — |
| 007-queued-w4-r2 | `spans` | 180 | 136 KiB | 0 B | 0 B | 136 KiB | — |
| 008-queued-w4-r3 | `runs` | 374 | 176 KiB | 328 KiB | 0 B | 504 KiB | ran |
| 008-queued-w4-r3 | `run_events` | 2244 | 1.26 MiB | 88 KiB | 0 B | 1.34 MiB | — |
| 008-queued-w4-r3 | `run_inputs` | 374 | 576 KiB | 40 KiB | 0 B | 616 KiB | — |
| 008-queued-w4-r3 | `tool_invocations` | 374 | 600 KiB | 72 KiB | 0 B | 672 KiB | — |
| 008-queued-w4-r3 | `jobs` | 374 | 584 KiB | 120 KiB | 0 B | 704 KiB | ran |
| 008-queued-w4-r3 | `traces` | 39 | 0 B | 0 B | 0 B | 0 B | — |
| 008-queued-w4-r3 | `spans` | 195 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |

## Worker contention

The worker count is a separate axis: the offered load is held constant and only the number of leasing processes changes. 🚨 This measures contention on one machine, one database and one clock. It is **not** a statement that multiple nodes are supported.

| Workers | Throughput/s | Largest share | Attempts / accepted job | Overlaps | Contention measured |
|---|---|---|---|---|---|
| 1 | 5.925 | 100.0 % | 1 | 0 | no |
| 2 | 5.929 | 73.6 % | 1 | 0 | yes |
| 4 | 5.96 | 32.9 % | 1 | 0 | yes |

Per worker process, per cell:

| Cell | Worker | PID | Jobs | Attempts |
|---|---|---|---|---|
| 000-queued-w1-r1 | worker-1 | 14669 | 368 | 368 |
| 001-queued-w1-r2 | worker-1 | 15225 | 368 | 368 |
| 002-queued-w1-r3 | worker-1 | 15657 | 368 | 368 |
| 003-queued-w2-r1 | worker-1 | 16144 | 264 | 264 |
| 003-queued-w2-r1 | worker-2 | 16149 | 104 | 104 |
| 004-queued-w2-r2 | worker-1 | 16636 | 279 | 279 |
| 004-queued-w2-r2 | worker-2 | 16637 | 93 | 93 |
| 005-queued-w2-r3 | worker-1 | 17166 | 273 | 273 |
| 005-queued-w2-r3 | worker-2 | 17169 | 95 | 95 |
| 006-queued-w4-r1 | worker-1 | 17698 | 125 | 125 |
| 006-queued-w4-r1 | worker-4 | 17709 | 123 | 123 |
| 006-queued-w4-r1 | worker-3 | 17706 | 64 | 64 |
| 006-queued-w4-r1 | worker-2 | 17699 | 57 | 57 |
| 007-queued-w4-r2 | worker-1 | 18183 | 119 | 119 |
| 007-queued-w4-r2 | worker-4 | 18193 | 111 | 111 |
| 007-queued-w4-r2 | worker-2 | 18184 | 76 | 76 |
| 007-queued-w4-r2 | worker-3 | 18185 | 70 | 70 |
| 008-queued-w4-r3 | worker-1 | 18695 | 124 | 124 |
| 008-queued-w4-r3 | worker-3 | 18697 | 116 | 116 |
| 008-queued-w4-r3 | worker-4 | 18700 | 72 | 72 |
| 008-queued-w4-r3 | worker-2 | 18696 | 62 | 62 |

## Telemetry coverage

Every measurement this apparatus knows how to take was available.

## First bottleneck

no ceiling was found inside the measured range

## What this report does not say

- Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.
- One machine, one database, one clock. Network partition, clock skew and inter-machine latency are out of scope, so nothing here says anything about multi-node behaviour.
- Autovacuum ran inside at least one window; those cells' byte growth is flagged and excluded from the averages. Their row counts remain usable.
