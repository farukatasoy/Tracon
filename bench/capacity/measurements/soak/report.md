# Capacity report — 20260914-213006-soak

Profile: `soak`.
1 cell(s): 1 complete, 0 incomplete, 0 invalid.

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
| buffered | empty | — | — | 8 | 1 | 4092 | 1033.08 | 1052.07 | 1081.19 | 2.315 | — (vacuum) | — |
| queued | empty | — | — | 8 | 1 | 4176 | 1286.41 | 1307.62 | 1340.16 | 2.315 | — (vacuum) | — |
| streaming | empty | — | — | 8 | 1 | 4247 | 1124.86 | 1182.87 | 1251.22 | 2.315 | — (vacuum) | — |

⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000). ⚠repeat marks one where the MERGED count clears the floor but the thinnest single repeat does not — the number is honest, but no individual window supported it. Percentiles are nearest rank over every repeat's samples merged together, never an average of the repeats' own percentiles. A bytes-per-run cell says how many repeats it came from when that is fewer than the repeat count; `1 rpt` is one window, not an average.

## What became of every request

`planned = not sent + sent` and `sent = accepted + rejected + failed + timed out` both close on every row; a report that cannot close them is hiding dropped load. A refusal or a timeout is a saturation finding and stays inside `sent` - it never leaves the distribution.

| Cell | Rate/s | Planned | Sent | Not sent | Accepted | Rejected | Timed out | Completed | Drain s | Queue wait p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-mixed-c8-empty-r1 | — | 12515 | 12515 | 0 | 12515 | 0 | 0 | 12515 | 0.19 | 97.79 |

## Reconciliation and resources

A store that refuses a write leaves the run green, so counting HTTP successes alone could report a clean measurement over lost data. Every cell therefore compares what was sent against what the store holds.

| Cell | Accepted runs | Terminal | Missing | Content mismatch | Sequence gaps | Live subs | Tenant bleed | Cross-tenant refused | Host peak RSS | Host CPU s |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-mixed-c8-empty-r1 | 12515 | 12515 | 0 | 0 | 0 | 4176 | 0 | 2 | 379.14 MiB | 323 |

## Write amplification

Rows and bytes together, table by table, with index and TOAST split out from the heap.

| Cell | Table | Rows | Heap | Index | TOAST | Total | Autovacuum |
|---|---|---|---|---|---|---|---|
| 000-mixed-c8-empty-r1 | `runs` | 12515 | 2.09 MiB | 7.73 MiB | 0 B | 9.82 MiB | ran |
| 000-mixed-c8-empty-r1 | `run_events` | 151536 | 61.91 MiB | 6.1 MiB | 0 B | 68.02 MiB | ran |
| 000-mixed-c8-empty-r1 | `run_inputs` | 12515 | 14.02 MiB | 1.27 MiB | 0 B | 15.29 MiB | ran |
| 000-mixed-c8-empty-r1 | `tool_invocations` | 12515 | 16.37 MiB | 1.76 MiB | 0 B | 18.13 MiB | ran |
| 000-mixed-c8-empty-r1 | `jobs` | 4176 | 5.54 MiB | 704 KiB | 0 B | 6.23 MiB | ran |
| 000-mixed-c8-empty-r1 | `traces` | 1209 | 584 KiB | 184 KiB | 0 B | 768 KiB | — |
| 000-mixed-c8-empty-r1 | `spans` | 6045 | 2.7 MiB | 520 KiB | 0 B | 3.2 MiB | ran |
| 000-mixed-c8-empty-r1 | `idempotency_keys` | 4092 | 3.69 MiB | 680 KiB | 0 B | 4.35 MiB | ran |

## Telemetry coverage

Every measurement this apparatus knows how to take was available.

## First bottleneck

no ceiling was found inside the measured range

## What this report does not say

- Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.
- One machine, one database, one clock. Network partition, clock skew and inter-machine latency are out of scope, so nothing here says anything about multi-node behaviour.
- Autovacuum ran inside at least one window; those cells' byte growth is flagged and excluded from the averages. Their row counts remain usable.
