# Capacity report — 20260914-204624-arrival

Profile: `arrival`.
12 cell(s): 12 complete, 0 incomplete, 0 invalid.

## Environment

| Field | Value |
|---|---|
| Commit | e44d89f58f5e36442d8a5e819e067f076c147373 |
| Tracon package | 0.0.0-capacity166.e44d89f |
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
| queued | empty | — | 1 | 16 | 3 | 180 | 1308.04 | 1332.3 ⚠repeat | 1605.92 ⚠ | 0.978 | 9.6 KiB (1 rpt) | 10.33 |
| queued | empty | — | 4 | 16 | 3 | 720 | 1292.06 | 1315.43 | 1332.72 ⚠ | 3.848 | — (vacuum) | 10.7 |
| queued | empty | — | 8 | 16 | 3 | 1440 | 4086.62 | 6870.51 | 7152.12 ⚠repeat | 6.383 | — (vacuum) | 10.63 |
| queued | empty | — | 16 | 16 | 3 | 1668 | 17422.32 | 17798.6 | 18337.3 ⚠repeat | 5.564 | — (vacuum) | 10.65 |

⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000). ⚠repeat marks one where the MERGED count clears the floor but the thinnest single repeat does not — the number is honest, but no individual window supported it. Percentiles are nearest rank over every repeat's samples merged together, never an average of the repeats' own percentiles. A bytes-per-run cell says how many repeats it came from when that is fewer than the repeat count; `1 rpt` is one window, not an average.

## What became of every request

`planned = not sent + sent` and `sent = accepted + rejected + failed + timed out` both close on every row; a report that cannot close them is hiding dropped load. A refusal or a timeout is a saturation finding and stays inside `sent` - it never leaves the distribution.

| Cell | Rate/s | Planned | Sent | Not sent | Accepted | Rejected | Timed out | Completed | Drain s | Queue wait p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-queued-a1-r1 | 1 | 60 | 60 | 0 | 60 | 0 | 0 | 60 | 0.05 | 105.08 |
| 001-queued-a1-r2 | 1 | 60 | 60 | 0 | 60 | 0 | 0 | 60 | 0.05 | 47.08 |
| 002-queued-a1-r3 | 1 | 60 | 60 | 0 | 60 | 0 | 0 | 60 | 0.07 | 113.52 |
| 003-queued-a4-r1 | 4 | 240 | 240 | 0 | 240 | 0 | 0 | 240 | 0.07 | 79.87 |
| 004-queued-a4-r2 | 4 | 240 | 240 | 0 | 240 | 0 | 0 | 240 | 0.06 | 102.73 |
| 005-queued-a4-r3 | 4 | 240 | 240 | 0 | 240 | 0 | 0 | 240 | 0.04 | 100.66 |
| 006-queued-a8-r1 | 8 | 480 | 480 | 0 | 480 | 0 | 0 | 480 | 0.07 | 5688.84 |
| 007-queued-a8-r2 | 8 | 480 | 480 | 0 | 480 | 0 | 0 | 480 | 0.07 | 5701.91 |
| 008-queued-a8-r3 | 8 | 480 | 480 | 0 | 480 | 0 | 0 | 480 | 0.07 | 5689.47 |
| 009-queued-a16-r1 | 16 | 960 | 559 | 401 | 557 | 0 | 0 | 557 | 0.07 | 16649.05 |
| 010-queued-a16-r2 | 16 | 960 | 558 | 402 | 558 | 0 | 0 | 558 | 0.06 | 16454.63 |
| 011-queued-a16-r3 | 16 | 960 | 555 | 405 | 553 | 0 | 0 | 553 | 0.07 | 16708.72 |

## Reconciliation and resources

A store that refuses a write leaves the run green, so counting HTTP successes alone could report a clean measurement over lost data. Every cell therefore compares what was sent against what the store holds.

| Cell | Accepted runs | Terminal | Missing | Content mismatch | Sequence gaps | Live subs | Tenant bleed | Cross-tenant refused | Host peak RSS | Host CPU s |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-queued-a1-r1 | 60 | 60 | 0 | 0 | 0 | 60 | 0 | 2 | 202.64 MiB | 1.9 |
| 001-queued-a1-r2 | 60 | 60 | 0 | 0 | 0 | 60 | 0 | 2 | 200.41 MiB | 2.8 |
| 002-queued-a1-r3 | 60 | 60 | 0 | 0 | 0 | 60 | 0 | 2 | 240.16 MiB | 2.9 |
| 003-queued-a4-r1 | 240 | 240 | 0 | 0 | 0 | 240 | 0 | 2 | 272.55 MiB | 8.4 |
| 004-queued-a4-r2 | 240 | 240 | 0 | 0 | 0 | 240 | 0 | 2 | 265.16 MiB | 8.6 |
| 005-queued-a4-r3 | 240 | 240 | 0 | 0 | 0 | 240 | 0 | 2 | 204.89 MiB | 6.4 |
| 006-queued-a8-r1 | 480 | 480 | 0 | 0 | 0 | 480 | 0 | 2 | 346.55 MiB | 17.3 |
| 007-queued-a8-r2 | 480 | 480 | 0 | 0 | 0 | 480 | 0 | 2 | 351.11 MiB | 17.5 |
| 008-queued-a8-r3 | 480 | 480 | 0 | 0 | 0 | 480 | 0 | 2 | 343.13 MiB | 17.3 |
| 009-queued-a16-r1 | 557 | 557 | 0 | 0 | 0 | 557 | 0 | 2 | 436.23 MiB | 25.6 |
| 010-queued-a16-r2 | 558 | 558 | 0 | 0 | 0 | 558 | 0 | 2 | 438.02 MiB | 25.8 |
| 011-queued-a16-r3 | 553 | 553 | 0 | 0 | 0 | 553 | 0 | 2 | 403.95 MiB | 24.7 |

## Write amplification

Rows and bytes together, table by table, with index and TOAST split out from the heap.

| Cell | Table | Rows | Heap | Index | TOAST | Total | Autovacuum |
|---|---|---|---|---|---|---|---|
| 000-queued-a1-r1 | `runs` | 60 | 0 B | 88 KiB | 0 B | 88 KiB | — |
| 000-queued-a1-r1 | `run_events` | 360 | 176 KiB | 16 KiB | 0 B | 192 KiB | — |
| 000-queued-a1-r1 | `run_inputs` | 60 | 112 KiB | 0 B | 0 B | 112 KiB | — |
| 000-queued-a1-r1 | `tool_invocations` | 60 | 104 KiB | 0 B | 0 B | 104 KiB | — |
| 000-queued-a1-r1 | `jobs` | 60 | 72 KiB | 8 KiB | 0 B | 80 KiB | — |
| 000-queued-a1-r1 | `traces` | 3 | 0 B | 0 B | 0 B | 0 B | — |
| 000-queued-a1-r1 | `spans` | 15 | 0 B | 0 B | 0 B | 0 B | — |
| 001-queued-a1-r2 | `runs` | 60 | 0 B | 32 KiB | 0 B | 32 KiB | ran |
| 001-queued-a1-r2 | `run_events` | 360 | 248 KiB | 16 KiB | 0 B | 264 KiB | — |
| 001-queued-a1-r2 | `run_inputs` | 60 | 128 KiB | 0 B | 0 B | 128 KiB | — |
| 001-queued-a1-r2 | `tool_invocations` | 60 | 128 KiB | 0 B | 0 B | 128 KiB | — |
| 001-queued-a1-r2 | `jobs` | 60 | 120 KiB | 0 B | 0 B | 120 KiB | ran |
| 001-queued-a1-r2 | `traces` | 4 | 0 B | 0 B | 0 B | 0 B | — |
| 001-queued-a1-r2 | `spans` | 20 | 16 KiB | 0 B | 0 B | 16 KiB | — |
| 002-queued-a1-r3 | `runs` | 60 | 0 B | 32 KiB | 0 B | 32 KiB | ran |
| 002-queued-a1-r3 | `run_events` | 360 | 240 KiB | 16 KiB | 0 B | 256 KiB | — |
| 002-queued-a1-r3 | `run_inputs` | 60 | 144 KiB | 0 B | 0 B | 144 KiB | — |
| 002-queued-a1-r3 | `tool_invocations` | 60 | 128 KiB | 0 B | 0 B | 128 KiB | — |
| 002-queued-a1-r3 | `jobs` | 60 | 120 KiB | 0 B | 0 B | 120 KiB | ran |
| 002-queued-a1-r3 | `traces` | 3 | 0 B | 0 B | 0 B | 0 B | — |
| 002-queued-a1-r3 | `spans` | 15 | 16 KiB | 0 B | 0 B | 16 KiB | — |
| 003-queued-a4-r1 | `runs` | 240 | 64 KiB | 232 KiB | 0 B | 296 KiB | ran |
| 003-queued-a4-r1 | `run_events` | 1440 | 752 KiB | 56 KiB | 0 B | 808 KiB | — |
| 003-queued-a4-r1 | `run_inputs` | 240 | 320 KiB | 40 KiB | 0 B | 360 KiB | — |
| 003-queued-a4-r1 | `tool_invocations` | 240 | 384 KiB | 56 KiB | 0 B | 440 KiB | — |
| 003-queued-a4-r1 | `jobs` | 240 | 344 KiB | 96 KiB | 0 B | 440 KiB | ran |
| 003-queued-a4-r1 | `traces` | 27 | 0 B | 0 B | 0 B | 0 B | — |
| 003-queued-a4-r1 | `spans` | 135 | 80 KiB | 0 B | 0 B | 80 KiB | — |
| 004-queued-a4-r2 | `runs` | 240 | 8 KiB | 240 KiB | 0 B | 248 KiB | ran |
| 004-queued-a4-r2 | `run_events` | 1440 | 808 KiB | 56 KiB | 0 B | 864 KiB | — |
| 004-queued-a4-r2 | `run_inputs` | 240 | 352 KiB | 40 KiB | 0 B | 392 KiB | — |
| 004-queued-a4-r2 | `tool_invocations` | 240 | 392 KiB | 56 KiB | 0 B | 448 KiB | — |
| 004-queued-a4-r2 | `jobs` | 240 | 304 KiB | 96 KiB | 0 B | 400 KiB | ran |
| 004-queued-a4-r2 | `traces` | 29 | 0 B | 0 B | 0 B | 0 B | — |
| 004-queued-a4-r2 | `spans` | 145 | 88 KiB | 0 B | 0 B | 88 KiB | — |
| 005-queued-a4-r3 | `runs` | 240 | 168 KiB | 240 KiB | 0 B | 408 KiB | ran |
| 005-queued-a4-r3 | `run_events` | 1440 | 808 KiB | 56 KiB | 0 B | 864 KiB | — |
| 005-queued-a4-r3 | `run_inputs` | 240 | 344 KiB | 40 KiB | 0 B | 384 KiB | — |
| 005-queued-a4-r3 | `tool_invocations` | 240 | 384 KiB | 56 KiB | 0 B | 440 KiB | — |
| 005-queued-a4-r3 | `jobs` | 240 | 360 KiB | 88 KiB | 0 B | 448 KiB | ran |
| 005-queued-a4-r3 | `traces` | 28 | 0 B | 0 B | 0 B | 0 B | — |
| 005-queued-a4-r3 | `spans` | 140 | 104 KiB | 0 B | 0 B | 104 KiB | — |
| 006-queued-a8-r1 | `runs` | 480 | 184 KiB | 384 KiB | 0 B | 568 KiB | ran |
| 006-queued-a8-r1 | `run_events` | 2880 | 1.55 MiB | 112 KiB | 0 B | 1.66 MiB | — |
| 006-queued-a8-r1 | `run_inputs` | 480 | 720 KiB | 64 KiB | 0 B | 784 KiB | — |
| 006-queued-a8-r1 | `tool_invocations` | 480 | 760 KiB | 88 KiB | 0 B | 848 KiB | — |
| 006-queued-a8-r1 | `jobs` | 480 | 744 KiB | 176 KiB | 0 B | 920 KiB | ran |
| 006-queued-a8-r1 | `traces` | 52 | 0 B | 0 B | 0 B | 0 B | — |
| 006-queued-a8-r1 | `spans` | 260 | 176 KiB | 16 KiB | 0 B | 192 KiB | — |
| 007-queued-a8-r2 | `runs` | 480 | 168 KiB | 392 KiB | 0 B | 560 KiB | ran |
| 007-queued-a8-r2 | `run_events` | 2880 | 1.43 MiB | 112 KiB | 0 B | 1.54 MiB | ran |
| 007-queued-a8-r2 | `run_inputs` | 480 | 592 KiB | 64 KiB | 0 B | 656 KiB | — |
| 007-queued-a8-r2 | `tool_invocations` | 480 | 712 KiB | 88 KiB | 0 B | 800 KiB | — |
| 007-queued-a8-r2 | `jobs` | 480 | 792 KiB | 184 KiB | 0 B | 976 KiB | ran |
| 007-queued-a8-r2 | `traces` | 40 | 0 B | 0 B | 0 B | 0 B | — |
| 007-queued-a8-r2 | `spans` | 200 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |
| 008-queued-a8-r3 | `runs` | 480 | 176 KiB | 400 KiB | 0 B | 576 KiB | ran |
| 008-queued-a8-r3 | `run_events` | 2880 | 1.48 MiB | 112 KiB | 0 B | 1.59 MiB | — |
| 008-queued-a8-r3 | `run_inputs` | 480 | 680 KiB | 64 KiB | 0 B | 744 KiB | — |
| 008-queued-a8-r3 | `tool_invocations` | 480 | 688 KiB | 88 KiB | 0 B | 776 KiB | — |
| 008-queued-a8-r3 | `jobs` | 480 | 816 KiB | 160 KiB | 0 B | 976 KiB | ran |
| 008-queued-a8-r3 | `traces` | 60 | 0 B | 0 B | 0 B | 0 B | — |
| 008-queued-a8-r3 | `spans` | 300 | 184 KiB | 32 KiB | 0 B | 216 KiB | — |
| 009-queued-a16-r1 | `runs` | 559 | 152 KiB | 512 KiB | 0 B | 664 KiB | ran |
| 009-queued-a16-r1 | `run_events` | 3354 | 2.45 MiB | 128 KiB | 0 B | 2.58 MiB | ran |
| 009-queued-a16-r1 | `run_inputs` | 559 | 1.08 MiB | 64 KiB | 0 B | 1.14 MiB | — |
| 009-queued-a16-r1 | `tool_invocations` | 559 | 1.17 MiB | 104 KiB | 0 B | 1.27 MiB | — |
| 009-queued-a16-r1 | `jobs` | 559 | 1.06 MiB | 184 KiB | 0 B | 1.24 MiB | ran |
| 009-queued-a16-r1 | `traces` | 61 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 009-queued-a16-r1 | `spans` | 305 | 160 KiB | 40 KiB | 0 B | 200 KiB | — |
| 010-queued-a16-r2 | `runs` | 558 | 328 KiB | 472 KiB | 0 B | 800 KiB | ran |
| 010-queued-a16-r2 | `run_events` | 3348 | 1.75 MiB | 128 KiB | 0 B | 1.88 MiB | ran |
| 010-queued-a16-r2 | `run_inputs` | 558 | 904 KiB | 64 KiB | 0 B | 968 KiB | — |
| 010-queued-a16-r2 | `tool_invocations` | 558 | 1.04 MiB | 104 KiB | 0 B | 1.14 MiB | — |
| 010-queued-a16-r2 | `jobs` | 558 | 1.02 MiB | 208 KiB | 0 B | 1.23 MiB | ran |
| 010-queued-a16-r2 | `traces` | 57 | 0 B | 0 B | 0 B | 0 B | — |
| 010-queued-a16-r2 | `spans` | 285 | 176 KiB | 40 KiB | 0 B | 216 KiB | — |
| 011-queued-a16-r3 | `runs` | 555 | 328 KiB | 504 KiB | 0 B | 832 KiB | ran |
| 011-queued-a16-r3 | `run_events` | 3330 | 1.8 MiB | 128 KiB | 0 B | 1.92 MiB | ran |
| 011-queued-a16-r3 | `run_inputs` | 555 | 1016 KiB | 64 KiB | 0 B | 1.05 MiB | — |
| 011-queued-a16-r3 | `tool_invocations` | 555 | 1.13 MiB | 104 KiB | 0 B | 1.23 MiB | — |
| 011-queued-a16-r3 | `jobs` | 555 | 1.09 MiB | 168 KiB | 0 B | 1.26 MiB | ran |
| 011-queued-a16-r3 | `traces` | 57 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 011-queued-a16-r3 | `spans` | 285 | 136 KiB | 40 KiB | 0 B | 176 KiB | — |

## Telemetry coverage

Every measurement this apparatus knows how to take was available.

## First bottleneck

the driver ran out of in-flight slots first in 009-queued-a16-r1; this is a client-side limit and is not reported as server capacity

## What this report does not say

- Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.
- One machine, one database, one clock. Network partition, clock skew and inter-machine latency are out of scope, so nothing here says anything about multi-node behaviour.
- Autovacuum ran inside at least one window; those cells' byte growth is flagged and excluded from the averages. Their row counts remain usable.
