# Capacity report — 20261029-191148-nodes

Profile: `nodes`.
12 cell(s): 12 complete, 0 incomplete, 0 invalid.

## Environment

| Field | Value |
|---|---|
| Commit | 647aa02e5b7d7b255607f5df93cd377ab93d8ccc |
| Tracon package | 0.0.0-capacity265.647aa02 |
| Operating system | Darwin 27.0.0 |
| Architecture | arm64 |
| Logical processors | 10 |
| Physical memory | 16 GiB |
| .NET runtime | 10.0.402 |
| PostgreSQL | 18.4 (Debian 18.4-1.pgdg12+1) |
| Database image | pgvector/pgvector:pg18 |

## Load points

| Scenario | Seed | Workers | Hosts | Rate/s | Concurrency | Repeats | n | p50 ms | p95 ms | p99 ms | Throughput/s | Bytes/run | Rows/run |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| buffered | empty | — | 1 | — | 16 | 3 | 2736 | 1053.47 | 1076.07 | 1088.06 ⚠repeat | 14.898 | 8.49 KiB (1 rpt) | 10.6 |
| buffered | empty | — | 2 | — | 16 | 3 | 2736 | 1057.29 | 1080.54 | 1095.19 ⚠repeat | 14.842 | — (vacuum) | 10.6 |
| queued | empty | — | 1 | — | 16 | 3 | 1344 | 2108.79 | 2357.17 | 2527.36 ⚠repeat | 6.98 | — (vacuum) | 10.67 |
| queued | empty | — | 2 | — | 16 | 3 | 2208 | 1320.19 | 1332.58 | 1339.13 ⚠repeat | 11.85 | — (vacuum) | 10.59 |

⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000). ⚠repeat marks one where the MERGED count clears the floor but the thinnest single repeat does not — the number is honest, but no individual window supported it. Percentiles are nearest rank over every repeat's samples merged together, never an average of the repeats' own percentiles. A bytes-per-run cell says how many repeats it came from when that is fewer than the repeat count; `1 rpt` is one window, not an average.

## What became of every request

`planned = not sent + sent` and `sent = accepted + rejected + failed + timed out` both close on every row; a report that cannot close them is hiding dropped load. A refusal or a timeout is a saturation finding and stays inside `sent` - it never leaves the distribution.

| Cell | Rate/s | Planned | Sent | Not sent | Accepted | Rejected | Timed out | Completed | Drain s | Queue wait p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-buffered-h1-r1 | — | 912 | 912 | 0 | 912 | 0 | 0 | 912 | 0.05 | — |
| 001-buffered-h1-r2 | — | 912 | 912 | 0 | 912 | 0 | 0 | 912 | 0.04 | — |
| 002-buffered-h1-r3 | — | 912 | 912 | 0 | 912 | 0 | 0 | 912 | 0.04 | — |
| 003-buffered-h2-r1 | — | 912 | 912 | 0 | 912 | 0 | 0 | 912 | 0.04 | — |
| 004-buffered-h2-r2 | — | 912 | 912 | 0 | 912 | 0 | 0 | 912 | 0.04 | — |
| 005-buffered-h2-r3 | — | 912 | 912 | 0 | 912 | 0 | 0 | 912 | 0.04 | — |
| 006-queued-h1-r1 | — | 448 | 448 | 0 | 448 | 0 | 0 | 448 | 0.05 | 1140.28 |
| 007-queued-h1-r2 | — | 448 | 448 | 0 | 448 | 0 | 0 | 448 | 0.06 | 1137.45 |
| 008-queued-h1-r3 | — | 448 | 448 | 0 | 448 | 0 | 0 | 448 | 0.05 | 1133.77 |
| 009-queued-h2-r1 | — | 736 | 736 | 0 | 736 | 0 | 0 | 736 | 0.05 | 102.12 |
| 010-queued-h2-r2 | — | 736 | 736 | 0 | 736 | 0 | 0 | 736 | 0.07 | 99.31 |
| 011-queued-h2-r3 | — | 736 | 736 | 0 | 736 | 0 | 0 | 736 | 0.06 | 100.38 |

## Reconciliation and resources

A store that refuses a write leaves the run green, so counting HTTP successes alone could report a clean measurement over lost data. Every cell therefore compares what was sent against what the store holds.

| Cell | Accepted runs | Terminal | Missing | Content mismatch | Sequence gaps | Live subs | Tenant bleed | Cross-tenant refused | Host peak RSS | Host CPU s |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-buffered-h1-r1 | 912 | 912 | 0 | 0 | 0 | 0 | 0 | 2 | 409.44 MiB | 7.3 |
| 001-buffered-h1-r2 | 912 | 912 | 0 | 0 | 0 | 0 | 0 | 2 | 419.16 MiB | 7.6 |
| 002-buffered-h1-r3 | 912 | 912 | 0 | 0 | 0 | 0 | 0 | 2 | 483.92 MiB | 7.6 |
| 003-buffered-h2-r1 | 912 | 912 | 0 | 0 | 0 | 0 | 0 | 2 | 540.97 MiB | 11.1 |
| 004-buffered-h2-r2 | 912 | 912 | 0 | 0 | 0 | 0 | 0 | 2 | 470.38 MiB | 12.6 |
| 005-buffered-h2-r3 | 912 | 912 | 0 | 0 | 0 | 0 | 0 | 2 | 526.61 MiB | 13.2 |
| 006-queued-h1-r1 | 448 | 448 | 0 | 0 | 0 | 448 | 0 | 2 | 367.89 MiB | 13.7 |
| 007-queued-h1-r2 | 448 | 448 | 0 | 0 | 0 | 448 | 0 | 2 | 415.19 MiB | 12.2 |
| 008-queued-h1-r3 | 448 | 448 | 0 | 0 | 0 | 448 | 0 | 2 | 463.88 MiB | 15.9 |
| 009-queued-h2-r1 | 736 | 736 | 0 | 0 | 0 | 736 | 0 | 2 | 805.11 MiB | 24.6 |
| 010-queued-h2-r2 | 736 | 736 | 0 | 0 | 0 | 736 | 0 | 2 | 738.23 MiB | 24.3 |
| 011-queued-h2-r3 | 736 | 736 | 0 | 0 | 0 | 736 | 0 | 2 | 800.98 MiB | 24.5 |

## Write amplification

Rows and bytes together, table by table, with index and TOAST split out from the heap.

| Cell | Table | Rows | Heap | Index | TOAST | Total | Autovacuum |
|---|---|---|---|---|---|---|---|
| 000-buffered-h1-r1 | `runs` | 912 | 160 KiB | 600 KiB | 0 B | 760 KiB | — |
| 000-buffered-h1-r1 | `run_events` | 5472 | 2.72 MiB | 216 KiB | 0 B | 2.93 MiB | — |
| 000-buffered-h1-r1 | `run_inputs` | 912 | 1008 KiB | 104 KiB | 0 B | 1.09 MiB | — |
| 000-buffered-h1-r1 | `tool_invocations` | 912 | 1.2 MiB | 160 KiB | 0 B | 1.36 MiB | — |
| 000-buffered-h1-r1 | `traces` | 94 | 160 KiB | 0 B | 0 B | 160 KiB | — |
| 000-buffered-h1-r1 | `spans` | 470 | 248 KiB | 48 KiB | 0 B | 296 KiB | — |
| 000-buffered-h1-r1 | `idempotency_keys` | 912 | 840 KiB | 184 KiB | 0 B | 1 MiB | — |
| 001-buffered-h1-r2 | `runs` | 912 | 144 KiB | 656 KiB | 0 B | 800 KiB | ran |
| 001-buffered-h1-r2 | `run_events` | 5472 | 2.7 MiB | 232 KiB | 0 B | 2.93 MiB | ran |
| 001-buffered-h1-r2 | `run_inputs` | 912 | 1.09 MiB | 104 KiB | 0 B | 1.2 MiB | — |
| 001-buffered-h1-r2 | `tool_invocations` | 912 | 1.21 MiB | 160 KiB | 0 B | 1.37 MiB | — |
| 001-buffered-h1-r2 | `traces` | 100 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 001-buffered-h1-r2 | `spans` | 500 | 296 KiB | 48 KiB | 0 B | 344 KiB | — |
| 001-buffered-h1-r2 | `idempotency_keys` | 912 | 792 KiB | 184 KiB | 0 B | 976 KiB | ran |
| 002-buffered-h1-r3 | `runs` | 912 | 136 KiB | 624 KiB | 0 B | 760 KiB | ran |
| 002-buffered-h1-r3 | `run_events` | 5472 | 2.63 MiB | 216 KiB | 0 B | 2.84 MiB | ran |
| 002-buffered-h1-r3 | `run_inputs` | 912 | 1.05 MiB | 104 KiB | 0 B | 1.15 MiB | — |
| 002-buffered-h1-r3 | `tool_invocations` | 912 | 1.26 MiB | 160 KiB | 0 B | 1.41 MiB | — |
| 002-buffered-h1-r3 | `traces` | 79 | 96 KiB | 0 B | 0 B | 96 KiB | — |
| 002-buffered-h1-r3 | `spans` | 395 | 232 KiB | 48 KiB | 0 B | 280 KiB | — |
| 002-buffered-h1-r3 | `idempotency_keys` | 912 | 808 KiB | 176 KiB | 0 B | 984 KiB | ran |
| 003-buffered-h2-r1 | `runs` | 912 | 32 KiB | 640 KiB | 0 B | 672 KiB | ran |
| 003-buffered-h2-r1 | `run_events` | 5472 | 2.7 MiB | 216 KiB | 0 B | 2.91 MiB | ran |
| 003-buffered-h2-r1 | `run_inputs` | 912 | 1.11 MiB | 104 KiB | 0 B | 1.21 MiB | — |
| 003-buffered-h2-r1 | `tool_invocations` | 912 | 1.24 MiB | 160 KiB | 0 B | 1.4 MiB | — |
| 003-buffered-h2-r1 | `traces` | 87 | 152 KiB | 0 B | 0 B | 152 KiB | — |
| 003-buffered-h2-r1 | `spans` | 435 | 248 KiB | 48 KiB | 0 B | 296 KiB | — |
| 003-buffered-h2-r1 | `idempotency_keys` | 912 | 368 KiB | 160 KiB | 0 B | 528 KiB | ran |
| 004-buffered-h2-r2 | `runs` | 912 | 176 KiB | 624 KiB | 0 B | 800 KiB | ran |
| 004-buffered-h2-r2 | `run_events` | 5472 | 2.67 MiB | 208 KiB | 0 B | 2.88 MiB | ran |
| 004-buffered-h2-r2 | `run_inputs` | 912 | 1.07 MiB | 104 KiB | 0 B | 1.17 MiB | — |
| 004-buffered-h2-r2 | `tool_invocations` | 912 | 1.25 MiB | 160 KiB | 0 B | 1.41 MiB | — |
| 004-buffered-h2-r2 | `traces` | 95 | 152 KiB | 0 B | 0 B | 152 KiB | — |
| 004-buffered-h2-r2 | `spans` | 475 | 272 KiB | 48 KiB | 0 B | 320 KiB | — |
| 004-buffered-h2-r2 | `idempotency_keys` | 912 | 744 KiB | 176 KiB | 0 B | 920 KiB | ran |
| 005-buffered-h2-r3 | `runs` | 912 | 184 KiB | 624 KiB | 0 B | 808 KiB | ran |
| 005-buffered-h2-r3 | `run_events` | 5472 | 2.65 MiB | 232 KiB | 0 B | 2.88 MiB | ran |
| 005-buffered-h2-r3 | `run_inputs` | 912 | 1.09 MiB | 104 KiB | 0 B | 1.2 MiB | — |
| 005-buffered-h2-r3 | `tool_invocations` | 912 | 1.23 MiB | 160 KiB | 0 B | 1.39 MiB | — |
| 005-buffered-h2-r3 | `traces` | 91 | 184 KiB | 0 B | 0 B | 184 KiB | — |
| 005-buffered-h2-r3 | `spans` | 455 | 280 KiB | 48 KiB | 0 B | 328 KiB | — |
| 005-buffered-h2-r3 | `idempotency_keys` | 912 | 856 KiB | 168 KiB | 0 B | 1 MiB | ran |
| 006-queued-h1-r1 | `runs` | 448 | 120 KiB | 424 KiB | 0 B | 544 KiB | ran |
| 006-queued-h1-r1 | `run_events` | 2688 | 1.41 MiB | 104 KiB | 0 B | 1.52 MiB | ran |
| 006-queued-h1-r1 | `run_inputs` | 448 | 640 KiB | 56 KiB | 0 B | 696 KiB | — |
| 006-queued-h1-r1 | `tool_invocations` | 448 | 568 KiB | 80 KiB | 0 B | 648 KiB | — |
| 006-queued-h1-r1 | `jobs` | 448 | 1.33 MiB | 168 KiB | 0 B | 1.49 MiB | ran |
| 006-queued-h1-r1 | `traces` | 60 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 006-queued-h1-r1 | `spans` | 300 | 184 KiB | 40 KiB | 0 B | 224 KiB | — |
| 007-queued-h1-r2 | `runs` | 448 | 128 KiB | 376 KiB | 0 B | 504 KiB | ran |
| 007-queued-h1-r2 | `run_events` | 2688 | 1.48 MiB | 104 KiB | 0 B | 1.58 MiB | — |
| 007-queued-h1-r2 | `run_inputs` | 448 | 656 KiB | 56 KiB | 0 B | 712 KiB | — |
| 007-queued-h1-r2 | `tool_invocations` | 448 | 712 KiB | 80 KiB | 0 B | 792 KiB | — |
| 007-queued-h1-r2 | `jobs` | 448 | 1.16 MiB | 152 KiB | 0 B | 1.31 MiB | ran |
| 007-queued-h1-r2 | `traces` | 38 | 0 B | 0 B | 0 B | 0 B | — |
| 007-queued-h1-r2 | `spans` | 190 | 112 KiB | 0 B | 0 B | 112 KiB | — |
| 008-queued-h1-r3 | `runs` | 448 | 192 KiB | 376 KiB | 0 B | 568 KiB | ran |
| 008-queued-h1-r3 | `run_events` | 2688 | 1.4 MiB | 104 KiB | 0 B | 1.5 MiB | — |
| 008-queued-h1-r3 | `run_inputs` | 448 | 696 KiB | 56 KiB | 0 B | 752 KiB | — |
| 008-queued-h1-r3 | `tool_invocations` | 448 | 712 KiB | 80 KiB | 0 B | 792 KiB | — |
| 008-queued-h1-r3 | `jobs` | 448 | 1.32 MiB | 160 KiB | 0 B | 1.48 MiB | ran |
| 008-queued-h1-r3 | `traces` | 52 | 0 B | 0 B | 0 B | 0 B | — |
| 008-queued-h1-r3 | `spans` | 260 | 160 KiB | 16 KiB | 0 B | 176 KiB | — |
| 009-queued-h2-r1 | `runs` | 736 | 232 KiB | 680 KiB | 0 B | 912 KiB | ran |
| 009-queued-h2-r1 | `run_events` | 4416 | 2.13 MiB | 176 KiB | 0 B | 2.3 MiB | ran |
| 009-queued-h2-r1 | `run_inputs` | 736 | 1008 KiB | 88 KiB | 0 B | 1.07 MiB | — |
| 009-queued-h2-r1 | `tool_invocations` | 736 | 1.06 MiB | 136 KiB | 0 B | 1.2 MiB | — |
| 009-queued-h2-r1 | `jobs` | 736 | 2.08 MiB | 248 KiB | 0 B | 2.32 MiB | ran |
| 009-queued-h2-r1 | `traces` | 68 | 104 KiB | 0 B | 0 B | 104 KiB | — |
| 009-queued-h2-r1 | `spans` | 340 | 224 KiB | 40 KiB | 0 B | 264 KiB | — |
| 010-queued-h2-r2 | `runs` | 736 | 288 KiB | 656 KiB | 0 B | 944 KiB | ran |
| 010-queued-h2-r2 | `run_events` | 4416 | 2.33 MiB | 168 KiB | 0 B | 2.49 MiB | — |
| 010-queued-h2-r2 | `run_inputs` | 736 | 968 KiB | 88 KiB | 0 B | 1.03 MiB | — |
| 010-queued-h2-r2 | `tool_invocations` | 736 | 1.13 MiB | 136 KiB | 0 B | 1.26 MiB | — |
| 010-queued-h2-r2 | `jobs` | 736 | 1.96 MiB | 264 KiB | 0 B | 2.22 MiB | ran |
| 010-queued-h2-r2 | `traces` | 73 | 104 KiB | 0 B | 0 B | 104 KiB | — |
| 010-queued-h2-r2 | `spans` | 365 | 216 KiB | 40 KiB | 0 B | 256 KiB | — |
| 011-queued-h2-r3 | `runs` | 736 | 328 KiB | 768 KiB | 0 B | 1.07 MiB | ran |
| 011-queued-h2-r3 | `run_events` | 4416 | 2.31 MiB | 168 KiB | 0 B | 2.48 MiB | — |
| 011-queued-h2-r3 | `run_inputs` | 736 | 648 KiB | 88 KiB | 0 B | 736 KiB | — |
| 011-queued-h2-r3 | `tool_invocations` | 736 | 1.08 MiB | 136 KiB | 0 B | 1.21 MiB | — |
| 011-queued-h2-r3 | `jobs` | 736 | 1.94 MiB | 264 KiB | 0 B | 2.2 MiB | ran |
| 011-queued-h2-r3 | `traces` | 76 | 136 KiB | 0 B | 0 B | 136 KiB | — |
| 011-queued-h2-r3 | `spans` | 380 | 208 KiB | 48 KiB | 0 B | 256 KiB | — |

## Telemetry coverage

These measurements were **unavailable** and are written as such, never as zero:

- `queue.wait`: no queued job recorded a start time

## First bottleneck

no ceiling was found inside the measured range

## What this report does not say

- Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.
- The hosts of the host axis are processes on one machine, with one database and one clock. Network partition, clock skew and inter-machine latency are out of scope, and nothing here says anything about more hosts than the axis names.
- For a cell of more than one host, "Host CPU s" is the processor time of the hosts added together and "Host peak RSS" is the sum of each host's own peak, an upper bound because the peaks need not coincide.
- Autovacuum ran inside at least one window; those cells' byte growth is flagged and excluded from the averages. Their row counts remain usable.
