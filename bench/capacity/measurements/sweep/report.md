# Capacity report — 20260914-190639-sweep

Profile: `sweep`.
72 cell(s): 72 complete, 0 incomplete, 0 invalid.

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
| buffered | empty | — | — | 1 | 3 | 174 | 1044.1 | 1057.33 ⚠repeat | 1072.81 ⚠ | 0.941 | 8.55 KiB (1 rpt) | 10.48 |
| buffered | empty | — | — | 8 | 3 | 1384 | 1048.19 | 1084.07 | 1188.34 ⚠repeat | 7.468 | — (vacuum) | 10.6 |
| buffered | empty | — | — | 32 | 3 | 5472 | 1060.14 | 1107.93 | 1155.07 | 29.489 | — (vacuum) | 10.57 |
| buffered | empty | — | — | 64 | 3 | 10944 | 1054.97 | 1119.72 | 1183.37 | 59.078 | — (vacuum) | 10.61 |
| buffered | full | — | — | 1 | 3 | 171 | 1035.36 | 1091.33 ⚠repeat | 1720.56 ⚠ | 0.929 | 8.41 KiB (2 rpt) | 10.67 |
| buffered | full | — | — | 8 | 3 | 1392 | 1047.2 | 1064.39 | 1096.31 ⚠repeat | 7.497 | — (vacuum) | 10.59 |
| buffered | full | — | — | 32 | 3 | 5472 | 1055.78 | 1104.36 | 1144.99 | 29.604 | — (vacuum) | 10.61 |
| buffered | full | — | — | 64 | 3 | 10944 | 1054.88 | 1118.74 | 1191.5 | 59.107 | — (vacuum) | 10.62 |
| queued | empty | — | — | 1 | 3 | 140 | 1308.97 | 1329.56 ⚠repeat | 1378.72 ⚠ | 0.753 | 10.26 KiB (1 rpt) | 10.94 |
| queued | empty | — | — | 8 | 3 | 1104 | 1317.63 | 1336.21 | 1366.87 ⚠repeat | 5.931 | — (vacuum) | 10.63 |
| queued | empty | — | — | 32 | 3 | 1392 | 4456.82 | 4494.47 | 4539.85 ⚠repeat | 6.746 | — (vacuum) | 10.62 |
| queued | empty | — | — | 64 | 3 | 1488 | 8772.72 | 8945.64 | 9002.65 ⚠repeat | 6.31 | — (vacuum) | 10.52 |
| queued | full | — | — | 1 | 3 | 138 | 1313.47 | 1350.97 ⚠repeat | 1370.05 ⚠ | 0.742 | — (vacuum) | 10.43 |
| queued | full | — | — | 8 | 3 | 1104 | 1316.94 | 1331.1 | 1335.06 ⚠repeat | 5.945 | — (vacuum) | 10.6 |
| queued | full | — | — | 32 | 3 | 1392 | 4446.31 | 4509.16 | 4536.57 ⚠repeat | 6.744 | — (vacuum) | 10.58 |
| queued | full | — | — | 64 | 3 | 1456 | 8851.5 | 9375.92 | 10429.23 ⚠repeat | 6.207 | — (vacuum) | 10.59 |
| streaming | empty | — | — | 1 | 3 | 153 | 1185.44 | 1226.11 ⚠repeat | 1249.68 ⚠ | 0.826 | 13.96 KiB (1 rpt) | 27.78 |
| streaming | empty | — | — | 8 | 3 | 1192 | 1214.64 | 1276.52 | 1346.61 ⚠repeat | 6.441 | — (vacuum) | 27.58 |
| streaming | empty | — | — | 32 | 3 | 5226 | 1104.01 | 1188.93 | 1240.88 | 28.095 | — (vacuum) | 27.65 |
| streaming | empty | — | — | 64 | 3 | 10718 | 1074.23 | 1135.51 | 1225.91 | 57.443 | — (vacuum) | 27.6 |
| streaming | full | — | — | 1 | 3 | 153 | 1192.44 | 1229.62 ⚠repeat | 1252.98 ⚠ | 0.821 | 14.22 KiB | 27.43 |
| streaming | full | — | — | 8 | 3 | 1200 | 1205.33 | 1238.15 | 1253.09 ⚠repeat | 6.501 | 14.93 KiB | 27.6 |
| streaming | full | — | — | 32 | 3 | 5332 | 1084.97 | 1151.53 | 1222.64 | 28.666 | 14.75 KiB (1 rpt) | 27.59 |
| streaming | full | — | — | 64 | 3 | 10626 | 1088.82 | 1136.76 | 1224.73 | 57.091 | — (vacuum) | 27.63 |

⚠ marks a percentile computed from fewer samples than its floor (p95: 100, p99: 1000). ⚠repeat marks one where the MERGED count clears the floor but the thinnest single repeat does not — the number is honest, but no individual window supported it. Percentiles are nearest rank over every repeat's samples merged together, never an average of the repeats' own percentiles. A bytes-per-run cell says how many repeats it came from when that is fewer than the repeat count; `1 rpt` is one window, not an average.

## What became of every request

`planned = not sent + sent` and `sent = accepted + rejected + failed + timed out` both close on every row; a report that cannot close them is hiding dropped load. A refusal or a timeout is a saturation finding and stays inside `sent` - it never leaves the distribution.

| Cell | Rate/s | Planned | Sent | Not sent | Accepted | Rejected | Timed out | Completed | Drain s | Queue wait p95 ms |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-buffered-c1-empty-r1 | — | 58 | 58 | 0 | 58 | 0 | 0 | 58 | 0.04 | — |
| 001-buffered-c1-empty-r2 | — | 58 | 58 | 0 | 58 | 0 | 0 | 58 | 0.06 | — |
| 002-buffered-c1-empty-r3 | — | 58 | 58 | 0 | 58 | 0 | 0 | 58 | 0.06 | — |
| 003-buffered-c1-full-r1 | — | 55 | 55 | 0 | 55 | 0 | 0 | 55 | 0.06 | — |
| 004-buffered-c1-full-r2 | — | 58 | 58 | 0 | 58 | 0 | 0 | 58 | 0.05 | — |
| 005-buffered-c1-full-r3 | — | 58 | 58 | 0 | 58 | 0 | 0 | 58 | 0.06 | — |
| 006-buffered-c8-empty-r1 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.05 | — |
| 007-buffered-c8-empty-r2 | — | 456 | 456 | 0 | 456 | 0 | 0 | 456 | 0.04 | — |
| 008-buffered-c8-empty-r3 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.06 | — |
| 009-buffered-c8-full-r1 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.05 | — |
| 010-buffered-c8-full-r2 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.06 | — |
| 011-buffered-c8-full-r3 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.05 | — |
| 012-buffered-c32-empty-r1 | — | 1824 | 1824 | 0 | 1824 | 0 | 0 | 1824 | 0.06 | — |
| 013-buffered-c32-empty-r2 | — | 1824 | 1824 | 0 | 1824 | 0 | 0 | 1824 | 0.05 | — |
| 014-buffered-c32-empty-r3 | — | 1824 | 1824 | 0 | 1824 | 0 | 0 | 1824 | 0.05 | — |
| 015-buffered-c32-full-r1 | — | 1824 | 1824 | 0 | 1824 | 0 | 0 | 1824 | 0.06 | — |
| 016-buffered-c32-full-r2 | — | 1824 | 1824 | 0 | 1824 | 0 | 0 | 1824 | 0.05 | — |
| 017-buffered-c32-full-r3 | — | 1824 | 1824 | 0 | 1824 | 0 | 0 | 1824 | 0.07 | — |
| 018-buffered-c64-empty-r1 | — | 3648 | 3648 | 0 | 3648 | 0 | 0 | 3648 | 0.06 | — |
| 019-buffered-c64-empty-r2 | — | 3648 | 3648 | 0 | 3648 | 0 | 0 | 3648 | 0.07 | — |
| 020-buffered-c64-empty-r3 | — | 3648 | 3648 | 0 | 3648 | 0 | 0 | 3648 | 0.08 | — |
| 021-buffered-c64-full-r1 | — | 3648 | 3648 | 0 | 3648 | 0 | 0 | 3648 | 0.07 | — |
| 022-buffered-c64-full-r2 | — | 3648 | 3648 | 0 | 3648 | 0 | 0 | 3648 | 0.06 | — |
| 023-buffered-c64-full-r3 | — | 3648 | 3648 | 0 | 3648 | 0 | 0 | 3648 | 0.08 | — |
| 024-streaming-c1-empty-r1 | — | 51 | 51 | 0 | 51 | 0 | 0 | 51 | 0.05 | — |
| 025-streaming-c1-empty-r2 | — | 51 | 51 | 0 | 51 | 0 | 0 | 51 | 0.06 | — |
| 026-streaming-c1-empty-r3 | — | 51 | 51 | 0 | 51 | 0 | 0 | 51 | 0.05 | — |
| 027-streaming-c1-full-r1 | — | 51 | 51 | 0 | 51 | 0 | 0 | 51 | 0.06 | — |
| 028-streaming-c1-full-r2 | — | 51 | 51 | 0 | 51 | 0 | 0 | 51 | 0.07 | — |
| 029-streaming-c1-full-r3 | — | 51 | 51 | 0 | 51 | 0 | 0 | 51 | 0.04 | — |
| 030-streaming-c8-empty-r1 | — | 400 | 400 | 0 | 400 | 0 | 0 | 400 | 0.05 | — |
| 031-streaming-c8-empty-r2 | — | 392 | 392 | 0 | 392 | 0 | 0 | 392 | 0.05 | — |
| 032-streaming-c8-empty-r3 | — | 400 | 400 | 0 | 400 | 0 | 0 | 400 | 0.04 | — |
| 033-streaming-c8-full-r1 | — | 400 | 400 | 0 | 400 | 0 | 0 | 400 | 0.06 | — |
| 034-streaming-c8-full-r2 | — | 400 | 400 | 0 | 400 | 0 | 0 | 400 | 0.07 | — |
| 035-streaming-c8-full-r3 | — | 400 | 400 | 0 | 400 | 0 | 0 | 400 | 0.05 | — |
| 036-streaming-c32-empty-r1 | — | 1728 | 1728 | 0 | 1728 | 0 | 0 | 1728 | 0.05 | — |
| 037-streaming-c32-empty-r2 | — | 1728 | 1728 | 0 | 1728 | 0 | 0 | 1728 | 0.05 | — |
| 038-streaming-c32-empty-r3 | — | 1770 | 1770 | 0 | 1770 | 0 | 0 | 1770 | 0.09 | — |
| 039-streaming-c32-full-r1 | — | 1760 | 1760 | 0 | 1760 | 0 | 0 | 1760 | 0.08 | — |
| 040-streaming-c32-full-r2 | — | 1792 | 1792 | 0 | 1792 | 0 | 0 | 1792 | 0.05 | — |
| 041-streaming-c32-full-r3 | — | 1780 | 1780 | 0 | 1780 | 0 | 0 | 1780 | 0.05 | — |
| 042-streaming-c64-empty-r1 | — | 3599 | 3599 | 0 | 3599 | 0 | 0 | 3599 | 0.09 | — |
| 043-streaming-c64-empty-r2 | — | 3594 | 3594 | 0 | 3594 | 0 | 0 | 3594 | 0.13 | — |
| 044-streaming-c64-empty-r3 | — | 3525 | 3525 | 0 | 3525 | 0 | 0 | 3525 | 0.13 | — |
| 045-streaming-c64-full-r1 | — | 3520 | 3520 | 0 | 3520 | 0 | 0 | 3520 | 0.11 | — |
| 046-streaming-c64-full-r2 | — | 3522 | 3522 | 0 | 3522 | 0 | 0 | 3522 | 0.09 | — |
| 047-streaming-c64-full-r3 | — | 3584 | 3584 | 0 | 3584 | 0 | 0 | 3584 | 0.07 | — |
| 048-queued-c1-empty-r1 | — | 47 | 47 | 0 | 47 | 0 | 0 | 47 | 0.06 | 106.53 |
| 049-queued-c1-empty-r2 | — | 46 | 46 | 0 | 46 | 0 | 0 | 46 | 0.06 | 105.62 |
| 050-queued-c1-empty-r3 | — | 47 | 47 | 0 | 47 | 0 | 0 | 47 | 0.06 | 102.13 |
| 051-queued-c1-full-r1 | — | 46 | 46 | 0 | 46 | 0 | 0 | 46 | 0.05 | 102.62 |
| 052-queued-c1-full-r2 | — | 46 | 46 | 0 | 46 | 0 | 0 | 46 | 0.06 | 103.56 |
| 053-queued-c1-full-r3 | — | 46 | 46 | 0 | 46 | 0 | 0 | 46 | 0.07 | 105.35 |
| 054-queued-c8-empty-r1 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.07 | 111.73 |
| 055-queued-c8-empty-r2 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.07 | 106.59 |
| 056-queued-c8-empty-r3 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.07 | 117.87 |
| 057-queued-c8-full-r1 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.07 | 116.5 |
| 058-queued-c8-full-r2 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.06 | 112.3 |
| 059-queued-c8-full-r3 | — | 368 | 368 | 0 | 368 | 0 | 0 | 368 | 0.05 | 119.37 |
| 060-queued-c32-empty-r1 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.06 | 3324.41 |
| 061-queued-c32-empty-r2 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.07 | 3360.41 |
| 062-queued-c32-empty-r3 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.07 | 3337.96 |
| 063-queued-c32-full-r1 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.06 | 3335.2 |
| 064-queued-c32-full-r2 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.07 | 3326.11 |
| 065-queued-c32-full-r3 | — | 464 | 464 | 0 | 464 | 0 | 0 | 464 | 0.07 | 3329.35 |
| 066-queued-c64-empty-r1 | — | 496 | 496 | 0 | 496 | 0 | 0 | 496 | 0.07 | 7714.77 |
| 067-queued-c64-empty-r2 | — | 496 | 496 | 0 | 496 | 0 | 0 | 496 | 0.07 | 7715.05 |
| 068-queued-c64-empty-r3 | — | 496 | 496 | 0 | 496 | 0 | 0 | 496 | 0.06 | 7728.12 |
| 069-queued-c64-full-r1 | — | 480 | 480 | 0 | 480 | 0 | 0 | 480 | 0.06 | 9231.21 |
| 070-queued-c64-full-r2 | — | 488 | 488 | 0 | 488 | 0 | 0 | 488 | 0.07 | 7961.96 |
| 071-queued-c64-full-r3 | — | 488 | 488 | 0 | 488 | 0 | 0 | 488 | 0.07 | 7909.19 |

## Reconciliation and resources

A store that refuses a write leaves the run green, so counting HTTP successes alone could report a clean measurement over lost data. Every cell therefore compares what was sent against what the store holds.

| Cell | Accepted runs | Terminal | Missing | Content mismatch | Sequence gaps | Live subs | Tenant bleed | Cross-tenant refused | Host peak RSS | Host CPU s |
|---|---|---|---|---|---|---|---|---|---|---|
| 000-buffered-c1-empty-r1 | 58 | 58 | 0 | 0 | 0 | 0 | 0 | 2 | 142.02 MiB | 3.6 |
| 001-buffered-c1-empty-r2 | 58 | 58 | 0 | 0 | 0 | 0 | 0 | 2 | 142.36 MiB | 4 |
| 002-buffered-c1-empty-r3 | 58 | 58 | 0 | 0 | 0 | 0 | 0 | 2 | 150.58 MiB | 4.1 |
| 003-buffered-c1-full-r1 | 55 | 55 | 0 | 0 | 0 | 0 | 0 | 2 | 175.2 MiB | 3.6 |
| 004-buffered-c1-full-r2 | 58 | 58 | 0 | 0 | 0 | 0 | 0 | 2 | 134.67 MiB | 3.2 |
| 005-buffered-c1-full-r3 | 58 | 58 | 0 | 0 | 0 | 0 | 0 | 2 | 146.28 MiB | 3.4 |
| 006-buffered-c8-empty-r1 | 464 | 464 | 0 | 0 | 0 | 0 | 0 | 2 | 198.7 MiB | 5 |
| 007-buffered-c8-empty-r2 | 456 | 456 | 0 | 0 | 0 | 0 | 0 | 2 | 196.19 MiB | 4.7 |
| 008-buffered-c8-empty-r3 | 464 | 464 | 0 | 0 | 0 | 0 | 0 | 2 | 197.73 MiB | 5.5 |
| 009-buffered-c8-full-r1 | 464 | 464 | 0 | 0 | 0 | 0 | 0 | 2 | 198.95 MiB | 5.5 |
| 010-buffered-c8-full-r2 | 464 | 464 | 0 | 0 | 0 | 0 | 0 | 2 | 195.7 MiB | 4.6 |
| 011-buffered-c8-full-r3 | 464 | 464 | 0 | 0 | 0 | 0 | 0 | 2 | 171.11 MiB | 5.2 |
| 012-buffered-c32-empty-r1 | 1824 | 1824 | 0 | 0 | 0 | 0 | 0 | 2 | 450.89 MiB | 11 |
| 013-buffered-c32-empty-r2 | 1824 | 1824 | 0 | 0 | 0 | 0 | 0 | 2 | 433.59 MiB | 12 |
| 014-buffered-c32-empty-r3 | 1824 | 1824 | 0 | 0 | 0 | 0 | 0 | 2 | 395 MiB | 11 |
| 015-buffered-c32-full-r1 | 1824 | 1824 | 0 | 0 | 0 | 0 | 0 | 2 | 408.48 MiB | 11.6 |
| 016-buffered-c32-full-r2 | 1824 | 1824 | 0 | 0 | 0 | 0 | 0 | 2 | 440.34 MiB | 11 |
| 017-buffered-c32-full-r3 | 1824 | 1824 | 0 | 0 | 0 | 0 | 0 | 2 | 430.95 MiB | 11.6 |
| 018-buffered-c64-empty-r1 | 3648 | 3648 | 0 | 0 | 0 | 0 | 0 | 2 | 602.38 MiB | 19.3 |
| 019-buffered-c64-empty-r2 | 3648 | 3648 | 0 | 0 | 0 | 0 | 0 | 2 | 634.22 MiB | 18.7 |
| 020-buffered-c64-empty-r3 | 3648 | 3648 | 0 | 0 | 0 | 0 | 0 | 2 | 610.17 MiB | 19.3 |
| 021-buffered-c64-full-r1 | 3648 | 3648 | 0 | 0 | 0 | 0 | 0 | 2 | 579.36 MiB | 18.4 |
| 022-buffered-c64-full-r2 | 3648 | 3648 | 0 | 0 | 0 | 0 | 0 | 2 | 605.48 MiB | 18.8 |
| 023-buffered-c64-full-r3 | 3648 | 3648 | 0 | 0 | 0 | 0 | 0 | 2 | 605.47 MiB | 19.4 |
| 024-streaming-c1-empty-r1 | 51 | 51 | 0 | 0 | 0 | 0 | 0 | 2 | 150.14 MiB | 5 |
| 025-streaming-c1-empty-r2 | 51 | 51 | 0 | 0 | 0 | 0 | 0 | 2 | 154.7 MiB | 5 |
| 026-streaming-c1-empty-r3 | 51 | 51 | 0 | 0 | 0 | 0 | 0 | 2 | 145.31 MiB | 5 |
| 027-streaming-c1-full-r1 | 51 | 51 | 0 | 0 | 0 | 0 | 0 | 2 | 180.38 MiB | 5.2 |
| 028-streaming-c1-full-r2 | 51 | 51 | 0 | 0 | 0 | 0 | 0 | 2 | 143.98 MiB | 5.3 |
| 029-streaming-c1-full-r3 | 51 | 51 | 0 | 0 | 0 | 0 | 0 | 2 | 148.94 MiB | 5.1 |
| 030-streaming-c8-empty-r1 | 400 | 400 | 0 | 0 | 0 | 0 | 0 | 2 | 254.5 MiB | 10.1 |
| 031-streaming-c8-empty-r2 | 392 | 392 | 0 | 0 | 0 | 0 | 0 | 2 | 218.47 MiB | 9.9 |
| 032-streaming-c8-empty-r3 | 400 | 400 | 0 | 0 | 0 | 0 | 0 | 2 | 214.88 MiB | 10.6 |
| 033-streaming-c8-full-r1 | 400 | 400 | 0 | 0 | 0 | 0 | 0 | 2 | 250.94 MiB | 11.1 |
| 034-streaming-c8-full-r2 | 400 | 400 | 0 | 0 | 0 | 0 | 0 | 2 | 237.19 MiB | 10.6 |
| 035-streaming-c8-full-r3 | 400 | 400 | 0 | 0 | 0 | 0 | 0 | 2 | 223.97 MiB | 11.1 |
| 036-streaming-c32-empty-r1 | 1728 | 1728 | 0 | 0 | 0 | 0 | 0 | 2 | 410.78 MiB | 25.5 |
| 037-streaming-c32-empty-r2 | 1728 | 1728 | 0 | 0 | 0 | 0 | 0 | 2 | 450.34 MiB | 22.7 |
| 038-streaming-c32-empty-r3 | 1770 | 1770 | 0 | 0 | 0 | 0 | 0 | 2 | 390.47 MiB | 26.7 |
| 039-streaming-c32-full-r1 | 1760 | 1760 | 0 | 0 | 0 | 0 | 0 | 2 | 405.64 MiB | 25.1 |
| 040-streaming-c32-full-r2 | 1792 | 1792 | 0 | 0 | 0 | 0 | 0 | 2 | 419.09 MiB | 28 |
| 041-streaming-c32-full-r3 | 1780 | 1780 | 0 | 0 | 0 | 0 | 0 | 2 | 492.38 MiB | 28 |
| 042-streaming-c64-empty-r1 | 3599 | 3599 | 0 | 0 | 0 | 0 | 0 | 2 | 578.94 MiB | 45.7 |
| 043-streaming-c64-empty-r2 | 3594 | 3594 | 0 | 0 | 0 | 0 | 0 | 2 | 518.56 MiB | 43.2 |
| 044-streaming-c64-empty-r3 | 3525 | 3525 | 0 | 0 | 0 | 0 | 0 | 2 | 519.86 MiB | 42 |
| 045-streaming-c64-full-r1 | 3520 | 3520 | 0 | 0 | 0 | 0 | 0 | 2 | 555.11 MiB | 40.9 |
| 046-streaming-c64-full-r2 | 3522 | 3522 | 0 | 0 | 0 | 0 | 0 | 2 | 522.84 MiB | 42.1 |
| 047-streaming-c64-full-r3 | 3584 | 3584 | 0 | 0 | 0 | 0 | 0 | 2 | 505.41 MiB | 42.1 |
| 048-queued-c1-empty-r1 | 47 | 47 | 0 | 0 | 0 | 47 | 0 | 2 | 159.34 MiB | 4.4 |
| 049-queued-c1-empty-r2 | 46 | 46 | 0 | 0 | 0 | 46 | 0 | 2 | 150.59 MiB | 4.4 |
| 050-queued-c1-empty-r3 | 47 | 47 | 0 | 0 | 0 | 47 | 0 | 2 | 147.95 MiB | 4.5 |
| 051-queued-c1-full-r1 | 46 | 46 | 0 | 0 | 0 | 46 | 0 | 2 | 153.98 MiB | 4.3 |
| 052-queued-c1-full-r2 | 46 | 46 | 0 | 0 | 0 | 46 | 0 | 2 | 151.08 MiB | 4.3 |
| 053-queued-c1-full-r3 | 46 | 46 | 0 | 0 | 0 | 46 | 0 | 2 | 138.8 MiB | 4.3 |
| 054-queued-c8-empty-r1 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 213.75 MiB | 7.7 |
| 055-queued-c8-empty-r2 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 248.06 MiB | 8.3 |
| 056-queued-c8-empty-r3 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 336.11 MiB | 7.5 |
| 057-queued-c8-full-r1 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 367.52 MiB | 8 |
| 058-queued-c8-full-r2 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 312.61 MiB | 8.2 |
| 059-queued-c8-full-r3 | 368 | 368 | 0 | 0 | 0 | 368 | 0 | 2 | 319.59 MiB | 8.2 |
| 060-queued-c32-empty-r1 | 464 | 464 | 0 | 0 | 0 | 464 | 0 | 2 | 435.66 MiB | 10 |
| 061-queued-c32-empty-r2 | 464 | 464 | 0 | 0 | 0 | 464 | 0 | 2 | 424.48 MiB | 10 |
| 062-queued-c32-empty-r3 | 464 | 464 | 0 | 0 | 0 | 464 | 0 | 2 | 426.5 MiB | 9.7 |
| 063-queued-c32-full-r1 | 464 | 464 | 0 | 0 | 0 | 464 | 0 | 2 | 406.38 MiB | 9.8 |
| 064-queued-c32-full-r2 | 464 | 464 | 0 | 0 | 0 | 464 | 0 | 2 | 426.73 MiB | 9.9 |
| 065-queued-c32-full-r3 | 464 | 464 | 0 | 0 | 0 | 464 | 0 | 2 | 433.56 MiB | 10.2 |
| 066-queued-c64-empty-r1 | 496 | 496 | 0 | 0 | 0 | 496 | 0 | 2 | 507.3 MiB | 12.9 |
| 067-queued-c64-empty-r2 | 496 | 496 | 0 | 0 | 0 | 496 | 0 | 2 | 548.28 MiB | 12.6 |
| 068-queued-c64-empty-r3 | 496 | 496 | 0 | 0 | 0 | 496 | 0 | 2 | 554.13 MiB | 13 |
| 069-queued-c64-full-r1 | 480 | 480 | 0 | 0 | 0 | 480 | 0 | 2 | 451.78 MiB | 10.8 |
| 070-queued-c64-full-r2 | 488 | 488 | 0 | 0 | 0 | 488 | 0 | 2 | 434.58 MiB | 12.2 |
| 071-queued-c64-full-r3 | 488 | 488 | 0 | 0 | 0 | 488 | 0 | 2 | 451.13 MiB | 11.7 |

## Write amplification

Rows and bytes together, table by table, with index and TOAST split out from the heap.

| Cell | Table | Rows | Heap | Index | TOAST | Total | Autovacuum |
|---|---|---|---|---|---|---|---|
| 000-buffered-c1-empty-r1 | `runs` | 58 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 000-buffered-c1-empty-r1 | `run_events` | 348 | 184 KiB | 24 KiB | 0 B | 208 KiB | — |
| 000-buffered-c1-empty-r1 | `run_inputs` | 58 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 000-buffered-c1-empty-r1 | `tool_invocations` | 58 | 80 KiB | 0 B | 0 B | 80 KiB | — |
| 000-buffered-c1-empty-r1 | `traces` | 5 | 0 B | 0 B | 0 B | 0 B | — |
| 000-buffered-c1-empty-r1 | `spans` | 25 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 000-buffered-c1-empty-r1 | `idempotency_keys` | 58 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 001-buffered-c1-empty-r2 | `runs` | 58 | 48 KiB | 0 B | 0 B | 48 KiB | ran |
| 001-buffered-c1-empty-r2 | `run_events` | 348 | 184 KiB | 24 KiB | 0 B | 208 KiB | — |
| 001-buffered-c1-empty-r2 | `run_inputs` | 58 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 001-buffered-c1-empty-r2 | `tool_invocations` | 58 | 88 KiB | 0 B | 0 B | 88 KiB | — |
| 001-buffered-c1-empty-r2 | `traces` | 3 | 0 B | 0 B | 0 B | 0 B | — |
| 001-buffered-c1-empty-r2 | `spans` | 15 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 001-buffered-c1-empty-r2 | `idempotency_keys` | 58 | 56 KiB | 0 B | 0 B | 56 KiB | ran |
| 002-buffered-c1-empty-r3 | `runs` | 58 | 48 KiB | 0 B | 0 B | 48 KiB | ran |
| 002-buffered-c1-empty-r3 | `run_events` | 348 | 184 KiB | 24 KiB | 0 B | 208 KiB | — |
| 002-buffered-c1-empty-r3 | `run_inputs` | 58 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 002-buffered-c1-empty-r3 | `tool_invocations` | 58 | 80 KiB | 0 B | 0 B | 80 KiB | — |
| 002-buffered-c1-empty-r3 | `traces` | 6 | 8 KiB | 24 KiB | 0 B | 32 KiB | — |
| 002-buffered-c1-empty-r3 | `spans` | 30 | 48 KiB | 16 KiB | 0 B | 64 KiB | — |
| 002-buffered-c1-empty-r3 | `idempotency_keys` | 58 | 56 KiB | 0 B | 0 B | 56 KiB | ran |
| 003-buffered-c1-full-r1 | `runs` | 55 | 0 B | 16 KiB | 0 B | 16 KiB | — |
| 003-buffered-c1-full-r1 | `run_events` | 330 | 176 KiB | 24 KiB | 0 B | 200 KiB | — |
| 003-buffered-c1-full-r1 | `run_inputs` | 55 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 003-buffered-c1-full-r1 | `tool_invocations` | 55 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 003-buffered-c1-full-r1 | `traces` | 5 | 0 B | 0 B | 0 B | 0 B | — |
| 003-buffered-c1-full-r1 | `spans` | 25 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 003-buffered-c1-full-r1 | `idempotency_keys` | 55 | 56 KiB | 0 B | 0 B | 56 KiB | ran |
| 004-buffered-c1-full-r2 | `runs` | 58 | 0 B | 16 KiB | 0 B | 16 KiB | — |
| 004-buffered-c1-full-r2 | `run_events` | 348 | 184 KiB | 24 KiB | 0 B | 208 KiB | — |
| 004-buffered-c1-full-r2 | `run_inputs` | 58 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 004-buffered-c1-full-r2 | `tool_invocations` | 58 | 80 KiB | 0 B | 0 B | 80 KiB | — |
| 004-buffered-c1-full-r2 | `traces` | 5 | 0 B | 0 B | 0 B | 0 B | — |
| 004-buffered-c1-full-r2 | `spans` | 25 | 8 KiB | 0 B | 0 B | 8 KiB | — |
| 004-buffered-c1-full-r2 | `idempotency_keys` | 58 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 005-buffered-c1-full-r3 | `runs` | 58 | 0 B | 16 KiB | 0 B | 16 KiB | — |
| 005-buffered-c1-full-r3 | `run_events` | 348 | 192 KiB | 24 KiB | 0 B | 216 KiB | — |
| 005-buffered-c1-full-r3 | `run_inputs` | 58 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 005-buffered-c1-full-r3 | `tool_invocations` | 58 | 80 KiB | 0 B | 0 B | 80 KiB | — |
| 005-buffered-c1-full-r3 | `traces` | 9 | 8 KiB | 24 KiB | 0 B | 32 KiB | — |
| 005-buffered-c1-full-r3 | `spans` | 45 | 56 KiB | 16 KiB | 0 B | 72 KiB | — |
| 005-buffered-c1-full-r3 | `idempotency_keys` | 58 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 006-buffered-c8-empty-r1 | `runs` | 464 | 72 KiB | 328 KiB | 0 B | 400 KiB | ran |
| 006-buffered-c8-empty-r1 | `run_events` | 2784 | 1.33 MiB | 112 KiB | 0 B | 1.44 MiB | ran |
| 006-buffered-c8-empty-r1 | `run_inputs` | 464 | 560 KiB | 64 KiB | 0 B | 624 KiB | — |
| 006-buffered-c8-empty-r1 | `tool_invocations` | 464 | 632 KiB | 88 KiB | 0 B | 720 KiB | — |
| 006-buffered-c8-empty-r1 | `traces` | 44 | 0 B | 0 B | 0 B | 0 B | — |
| 006-buffered-c8-empty-r1 | `spans` | 220 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |
| 006-buffered-c8-empty-r1 | `idempotency_keys` | 464 | 424 KiB | 96 KiB | 0 B | 520 KiB | ran |
| 007-buffered-c8-empty-r2 | `runs` | 456 | 72 KiB | 336 KiB | 0 B | 408 KiB | ran |
| 007-buffered-c8-empty-r2 | `run_events` | 2736 | 1.37 MiB | 104 KiB | 0 B | 1.47 MiB | ran |
| 007-buffered-c8-empty-r2 | `run_inputs` | 456 | 544 KiB | 64 KiB | 0 B | 608 KiB | — |
| 007-buffered-c8-empty-r2 | `tool_invocations` | 456 | 640 KiB | 88 KiB | 0 B | 728 KiB | — |
| 007-buffered-c8-empty-r2 | `traces` | 50 | 0 B | 0 B | 0 B | 0 B | — |
| 007-buffered-c8-empty-r2 | `spans` | 250 | 128 KiB | 16 KiB | 0 B | 144 KiB | — |
| 007-buffered-c8-empty-r2 | `idempotency_keys` | 456 | 64 KiB | 104 KiB | 0 B | 168 KiB | ran |
| 008-buffered-c8-empty-r3 | `runs` | 464 | 32 KiB | 336 KiB | 0 B | 368 KiB | ran |
| 008-buffered-c8-empty-r3 | `run_events` | 2784 | 1.41 MiB | 104 KiB | 0 B | 1.51 MiB | — |
| 008-buffered-c8-empty-r3 | `run_inputs` | 464 | 536 KiB | 64 KiB | 0 B | 600 KiB | — |
| 008-buffered-c8-empty-r3 | `tool_invocations` | 464 | 664 KiB | 88 KiB | 0 B | 752 KiB | — |
| 008-buffered-c8-empty-r3 | `traces` | 43 | 0 B | 0 B | 0 B | 0 B | — |
| 008-buffered-c8-empty-r3 | `spans` | 215 | 104 KiB | 16 KiB | 0 B | 120 KiB | — |
| 008-buffered-c8-empty-r3 | `idempotency_keys` | 464 | 40 KiB | 104 KiB | 0 B | 144 KiB | ran |
| 009-buffered-c8-full-r1 | `runs` | 464 | 128 KiB | 320 KiB | 0 B | 448 KiB | — |
| 009-buffered-c8-full-r1 | `run_events` | 2784 | 1.38 MiB | 192 KiB | 0 B | 1.56 MiB | — |
| 009-buffered-c8-full-r1 | `run_inputs` | 464 | 560 KiB | 64 KiB | 0 B | 624 KiB | — |
| 009-buffered-c8-full-r1 | `tool_invocations` | 464 | 640 KiB | 88 KiB | 0 B | 728 KiB | — |
| 009-buffered-c8-full-r1 | `traces` | 46 | 0 B | 0 B | 0 B | 0 B | — |
| 009-buffered-c8-full-r1 | `spans` | 230 | 128 KiB | 16 KiB | 0 B | 144 KiB | — |
| 009-buffered-c8-full-r1 | `idempotency_keys` | 464 | 392 KiB | 104 KiB | 0 B | 496 KiB | ran |
| 010-buffered-c8-full-r2 | `runs` | 464 | 120 KiB | 328 KiB | 0 B | 448 KiB | — |
| 010-buffered-c8-full-r2 | `run_events` | 2784 | 1.38 MiB | 200 KiB | 0 B | 1.58 MiB | — |
| 010-buffered-c8-full-r2 | `run_inputs` | 464 | 568 KiB | 64 KiB | 0 B | 632 KiB | — |
| 010-buffered-c8-full-r2 | `tool_invocations` | 464 | 664 KiB | 88 KiB | 0 B | 752 KiB | — |
| 010-buffered-c8-full-r2 | `traces` | 47 | 0 B | 0 B | 0 B | 0 B | — |
| 010-buffered-c8-full-r2 | `spans` | 235 | 112 KiB | 32 KiB | 0 B | 144 KiB | — |
| 010-buffered-c8-full-r2 | `idempotency_keys` | 464 | 408 KiB | 112 KiB | 0 B | 520 KiB | ran |
| 011-buffered-c8-full-r3 | `runs` | 464 | 120 KiB | 328 KiB | 0 B | 448 KiB | — |
| 011-buffered-c8-full-r3 | `run_events` | 2784 | 1.4 MiB | 192 KiB | 0 B | 1.59 MiB | — |
| 011-buffered-c8-full-r3 | `run_inputs` | 464 | 552 KiB | 64 KiB | 0 B | 616 KiB | — |
| 011-buffered-c8-full-r3 | `tool_invocations` | 464 | 640 KiB | 88 KiB | 0 B | 728 KiB | — |
| 011-buffered-c8-full-r3 | `traces` | 45 | 0 B | 0 B | 0 B | 0 B | — |
| 011-buffered-c8-full-r3 | `spans` | 225 | 120 KiB | 32 KiB | 0 B | 152 KiB | — |
| 011-buffered-c8-full-r3 | `idempotency_keys` | 464 | 392 KiB | 104 KiB | 0 B | 496 KiB | ran |
| 012-buffered-c32-empty-r1 | `runs` | 1824 | 304 KiB | 1.23 MiB | 0 B | 1.53 MiB | ran |
| 012-buffered-c32-empty-r1 | `run_events` | 10944 | 5.41 MiB | 528 KiB | 0 B | 5.93 MiB | ran |
| 012-buffered-c32-empty-r1 | `run_inputs` | 1824 | 2.12 MiB | 184 KiB | 0 B | 2.3 MiB | — |
| 012-buffered-c32-empty-r1 | `tool_invocations` | 1824 | 2.43 MiB | 256 KiB | 0 B | 2.68 MiB | — |
| 012-buffered-c32-empty-r1 | `traces` | 192 | 256 KiB | 24 KiB | 0 B | 280 KiB | — |
| 012-buffered-c32-empty-r1 | `spans` | 960 | 560 KiB | 88 KiB | 0 B | 648 KiB | — |
| 012-buffered-c32-empty-r1 | `idempotency_keys` | 1824 | 1.63 MiB | 344 KiB | 0 B | 1.96 MiB | ran |
| 013-buffered-c32-empty-r2 | `runs` | 1824 | 264 KiB | 1.27 MiB | 0 B | 1.53 MiB | ran |
| 013-buffered-c32-empty-r2 | `run_events` | 10944 | 5.41 MiB | 544 KiB | 0 B | 5.94 MiB | ran |
| 013-buffered-c32-empty-r2 | `run_inputs` | 1824 | 1.66 MiB | 184 KiB | 0 B | 1.84 MiB | ran |
| 013-buffered-c32-empty-r2 | `tool_invocations` | 1824 | 2.22 MiB | 256 KiB | 0 B | 2.47 MiB | ran |
| 013-buffered-c32-empty-r2 | `traces` | 167 | 264 KiB | 24 KiB | 0 B | 288 KiB | — |
| 013-buffered-c32-empty-r2 | `spans` | 835 | 464 KiB | 72 KiB | 0 B | 536 KiB | — |
| 013-buffered-c32-empty-r2 | `idempotency_keys` | 1824 | 1.61 MiB | 304 KiB | 0 B | 1.91 MiB | ran |
| 014-buffered-c32-empty-r3 | `runs` | 1824 | 336 KiB | 1.25 MiB | 0 B | 1.58 MiB | ran |
| 014-buffered-c32-empty-r3 | `run_events` | 10944 | 5.48 MiB | 512 KiB | 0 B | 5.98 MiB | ran |
| 014-buffered-c32-empty-r3 | `run_inputs` | 1824 | 2.02 MiB | 184 KiB | 0 B | 2.2 MiB | ran |
| 014-buffered-c32-empty-r3 | `tool_invocations` | 1824 | 2.25 MiB | 256 KiB | 0 B | 2.5 MiB | ran |
| 014-buffered-c32-empty-r3 | `traces` | 162 | 248 KiB | 24 KiB | 0 B | 272 KiB | — |
| 014-buffered-c32-empty-r3 | `spans` | 810 | 448 KiB | 80 KiB | 0 B | 528 KiB | — |
| 014-buffered-c32-empty-r3 | `idempotency_keys` | 1824 | 1.59 MiB | 352 KiB | 0 B | 1.93 MiB | ran |
| 015-buffered-c32-full-r1 | `runs` | 1824 | 264 KiB | 1.37 MiB | 0 B | 1.63 MiB | ran |
| 015-buffered-c32-full-r1 | `run_events` | 10944 | 5.44 MiB | 768 KiB | 0 B | 6.19 MiB | — |
| 015-buffered-c32-full-r1 | `run_inputs` | 1824 | 2.11 MiB | 184 KiB | 0 B | 2.29 MiB | ran |
| 015-buffered-c32-full-r1 | `tool_invocations` | 1824 | 2.4 MiB | 256 KiB | 0 B | 2.65 MiB | ran |
| 015-buffered-c32-full-r1 | `traces` | 196 | 280 KiB | 24 KiB | 0 B | 304 KiB | — |
| 015-buffered-c32-full-r1 | `spans` | 980 | 544 KiB | 96 KiB | 0 B | 640 KiB | ran |
| 015-buffered-c32-full-r1 | `idempotency_keys` | 1824 | 1.54 MiB | 328 KiB | 0 B | 1.86 MiB | ran |
| 016-buffered-c32-full-r2 | `runs` | 1824 | 256 KiB | 1.39 MiB | 0 B | 1.64 MiB | — |
| 016-buffered-c32-full-r2 | `run_events` | 10944 | 5.43 MiB | 768 KiB | 0 B | 6.18 MiB | — |
| 016-buffered-c32-full-r2 | `run_inputs` | 1824 | 2.17 MiB | 184 KiB | 0 B | 2.35 MiB | — |
| 016-buffered-c32-full-r2 | `tool_invocations` | 1824 | 2.46 MiB | 256 KiB | 0 B | 2.71 MiB | — |
| 016-buffered-c32-full-r2 | `traces` | 183 | 208 KiB | 24 KiB | 0 B | 232 KiB | — |
| 016-buffered-c32-full-r2 | `spans` | 915 | 528 KiB | 72 KiB | 0 B | 600 KiB | — |
| 016-buffered-c32-full-r2 | `idempotency_keys` | 1824 | 1.57 MiB | 328 KiB | 0 B | 1.89 MiB | ran |
| 017-buffered-c32-full-r3 | `runs` | 1824 | 400 KiB | 1.37 MiB | 0 B | 1.76 MiB | — |
| 017-buffered-c32-full-r3 | `run_events` | 10944 | 5.44 MiB | 776 KiB | 0 B | 6.2 MiB | — |
| 017-buffered-c32-full-r3 | `run_inputs` | 1824 | 2.14 MiB | 184 KiB | 0 B | 2.32 MiB | — |
| 017-buffered-c32-full-r3 | `tool_invocations` | 1824 | 2.48 MiB | 256 KiB | 0 B | 2.73 MiB | — |
| 017-buffered-c32-full-r3 | `traces` | 180 | 80 KiB | 24 KiB | 0 B | 104 KiB | — |
| 017-buffered-c32-full-r3 | `spans` | 900 | 496 KiB | 88 KiB | 0 B | 584 KiB | — |
| 017-buffered-c32-full-r3 | `idempotency_keys` | 1824 | 1.39 MiB | 352 KiB | 0 B | 1.73 MiB | ran |
| 018-buffered-c64-empty-r1 | `runs` | 3648 | 808 KiB | 2.61 MiB | 0 B | 3.4 MiB | ran |
| 018-buffered-c64-empty-r1 | `run_events` | 21888 | 10.87 MiB | 1.34 MiB | 0 B | 12.2 MiB | ran |
| 018-buffered-c64-empty-r1 | `run_inputs` | 3648 | 4.19 MiB | 376 KiB | 0 B | 4.55 MiB | ran |
| 018-buffered-c64-empty-r1 | `tool_invocations` | 3648 | 3.96 MiB | 512 KiB | 0 B | 4.46 MiB | ran |
| 018-buffered-c64-empty-r1 | `traces` | 380 | 240 KiB | 72 KiB | 0 B | 312 KiB | — |
| 018-buffered-c64-empty-r1 | `spans` | 1900 | 1.03 MiB | 128 KiB | 0 B | 1.16 MiB | ran |
| 018-buffered-c64-empty-r1 | `idempotency_keys` | 3648 | 3.13 MiB | 688 KiB | 0 B | 3.8 MiB | ran |
| 019-buffered-c64-empty-r2 | `runs` | 3648 | 576 KiB | 2.59 MiB | 0 B | 3.16 MiB | ran |
| 019-buffered-c64-empty-r2 | `run_events` | 21888 | 10.84 MiB | 1.34 MiB | 0 B | 12.17 MiB | ran |
| 019-buffered-c64-empty-r2 | `run_inputs` | 3648 | 4.2 MiB | 376 KiB | 0 B | 4.57 MiB | — |
| 019-buffered-c64-empty-r2 | `tool_invocations` | 3648 | 4.97 MiB | 512 KiB | 0 B | 5.47 MiB | — |
| 019-buffered-c64-empty-r2 | `traces` | 374 | 336 KiB | 72 KiB | 0 B | 408 KiB | — |
| 019-buffered-c64-empty-r2 | `spans` | 1870 | 1.03 MiB | 128 KiB | 0 B | 1.16 MiB | — |
| 019-buffered-c64-empty-r2 | `idempotency_keys` | 3648 | 3.13 MiB | 680 KiB | 0 B | 3.79 MiB | ran |
| 020-buffered-c64-empty-r3 | `runs` | 3648 | 520 KiB | 2.63 MiB | 0 B | 3.13 MiB | ran |
| 020-buffered-c64-empty-r3 | `run_events` | 21888 | 10.9 MiB | 1.34 MiB | 0 B | 12.23 MiB | ran |
| 020-buffered-c64-empty-r3 | `run_inputs` | 3648 | 4.34 MiB | 376 KiB | 0 B | 4.71 MiB | — |
| 020-buffered-c64-empty-r3 | `tool_invocations` | 3648 | 4.99 MiB | 512 KiB | 0 B | 5.49 MiB | — |
| 020-buffered-c64-empty-r3 | `traces` | 364 | 400 KiB | 72 KiB | 0 B | 472 KiB | — |
| 020-buffered-c64-empty-r3 | `spans` | 1820 | 1.03 MiB | 128 KiB | 0 B | 1.16 MiB | — |
| 020-buffered-c64-empty-r3 | `idempotency_keys` | 3648 | 3.11 MiB | 688 KiB | 0 B | 3.78 MiB | ran |
| 021-buffered-c64-full-r1 | `runs` | 3648 | 720 KiB | 2.91 MiB | 0 B | 3.62 MiB | ran |
| 021-buffered-c64-full-r1 | `run_events` | 21888 | 11.08 MiB | 1.48 MiB | 0 B | 12.56 MiB | — |
| 021-buffered-c64-full-r1 | `run_inputs` | 3648 | 3.8 MiB | 368 KiB | 0 B | 4.16 MiB | ran |
| 021-buffered-c64-full-r1 | `tool_invocations` | 3648 | 4.37 MiB | 512 KiB | 0 B | 4.87 MiB | ran |
| 021-buffered-c64-full-r1 | `traces` | 367 | 216 KiB | 72 KiB | 0 B | 288 KiB | — |
| 021-buffered-c64-full-r1 | `spans` | 1835 | 984 KiB | 120 KiB | 0 B | 1.08 MiB | ran |
| 021-buffered-c64-full-r1 | `idempotency_keys` | 3648 | 2.95 MiB | 680 KiB | 0 B | 3.62 MiB | ran |
| 022-buffered-c64-full-r2 | `runs` | 3648 | 1008 KiB | 2.84 MiB | 0 B | 3.83 MiB | ran |
| 022-buffered-c64-full-r2 | `run_events` | 21888 | 11.03 MiB | 1.48 MiB | 0 B | 12.51 MiB | — |
| 022-buffered-c64-full-r2 | `run_inputs` | 3648 | 4.16 MiB | 368 KiB | 0 B | 4.52 MiB | ran |
| 022-buffered-c64-full-r2 | `tool_invocations` | 3648 | 4.65 MiB | 512 KiB | 0 B | 5.15 MiB | ran |
| 022-buffered-c64-full-r2 | `traces` | 383 | 320 KiB | 72 KiB | 0 B | 392 KiB | — |
| 022-buffered-c64-full-r2 | `spans` | 1915 | 1.05 MiB | 136 KiB | 0 B | 1.19 MiB | ran |
| 022-buffered-c64-full-r2 | `idempotency_keys` | 3648 | 3.21 MiB | 696 KiB | 0 B | 3.89 MiB | ran |
| 023-buffered-c64-full-r3 | `runs` | 3648 | 872 KiB | 2.86 MiB | 0 B | 3.71 MiB | — |
| 023-buffered-c64-full-r3 | `run_events` | 21888 | 10.87 MiB | 1.48 MiB | 0 B | 12.35 MiB | — |
| 023-buffered-c64-full-r3 | `run_inputs` | 3648 | 4.27 MiB | 376 KiB | 0 B | 4.63 MiB | — |
| 023-buffered-c64-full-r3 | `tool_invocations` | 3648 | 4.97 MiB | 512 KiB | 0 B | 5.47 MiB | — |
| 023-buffered-c64-full-r3 | `traces` | 383 | 320 KiB | 72 KiB | 0 B | 392 KiB | — |
| 023-buffered-c64-full-r3 | `spans` | 1915 | 1.04 MiB | 128 KiB | 0 B | 1.16 MiB | — |
| 023-buffered-c64-full-r3 | `idempotency_keys` | 3648 | 3.12 MiB | 672 KiB | 0 B | 3.77 MiB | ran |
| 024-streaming-c1-empty-r1 | `runs` | 51 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 024-streaming-c1-empty-r1 | `run_events` | 1224 | 464 KiB | 48 KiB | 0 B | 512 KiB | — |
| 024-streaming-c1-empty-r1 | `run_inputs` | 51 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 024-streaming-c1-empty-r1 | `tool_invocations` | 51 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 024-streaming-c1-empty-r1 | `traces` | 5 | 0 B | 0 B | 0 B | 0 B | — |
| 024-streaming-c1-empty-r1 | `spans` | 25 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 025-streaming-c1-empty-r2 | `runs` | 51 | 48 KiB | 0 B | 0 B | 48 KiB | ran |
| 025-streaming-c1-empty-r2 | `run_events` | 1224 | 464 KiB | 48 KiB | 0 B | 512 KiB | ran |
| 025-streaming-c1-empty-r2 | `run_inputs` | 51 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 025-streaming-c1-empty-r2 | `tool_invocations` | 51 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 025-streaming-c1-empty-r2 | `traces` | 8 | 0 B | 0 B | 0 B | 0 B | — |
| 025-streaming-c1-empty-r2 | `spans` | 40 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 026-streaming-c1-empty-r3 | `runs` | 51 | 48 KiB | 0 B | 0 B | 48 KiB | ran |
| 026-streaming-c1-empty-r3 | `run_events` | 1224 | 480 KiB | 48 KiB | 0 B | 528 KiB | ran |
| 026-streaming-c1-empty-r3 | `run_inputs` | 51 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 026-streaming-c1-empty-r3 | `tool_invocations` | 51 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 026-streaming-c1-empty-r3 | `traces` | 7 | 0 B | 0 B | 0 B | 0 B | — |
| 026-streaming-c1-empty-r3 | `spans` | 35 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 027-streaming-c1-full-r1 | `runs` | 51 | 0 B | 0 B | 0 B | 0 B | — |
| 027-streaming-c1-full-r1 | `run_events` | 1224 | 472 KiB | 80 KiB | 0 B | 552 KiB | — |
| 027-streaming-c1-full-r1 | `run_inputs` | 51 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 027-streaming-c1-full-r1 | `tool_invocations` | 51 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 027-streaming-c1-full-r1 | `traces` | 3 | 0 B | 0 B | 0 B | 0 B | — |
| 027-streaming-c1-full-r1 | `spans` | 15 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 028-streaming-c1-full-r2 | `runs` | 51 | 0 B | 0 B | 0 B | 0 B | — |
| 028-streaming-c1-full-r2 | `run_events` | 1224 | 464 KiB | 80 KiB | 0 B | 544 KiB | — |
| 028-streaming-c1-full-r2 | `run_inputs` | 51 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 028-streaming-c1-full-r2 | `tool_invocations` | 51 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 028-streaming-c1-full-r2 | `traces` | 4 | 8 KiB | 24 KiB | 0 B | 32 KiB | — |
| 028-streaming-c1-full-r2 | `spans` | 20 | 40 KiB | 16 KiB | 0 B | 56 KiB | — |
| 029-streaming-c1-full-r3 | `runs` | 51 | 0 B | 0 B | 0 B | 0 B | — |
| 029-streaming-c1-full-r3 | `run_events` | 1224 | 464 KiB | 80 KiB | 0 B | 544 KiB | — |
| 029-streaming-c1-full-r3 | `run_inputs` | 51 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 029-streaming-c1-full-r3 | `tool_invocations` | 51 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 029-streaming-c1-full-r3 | `traces` | 4 | 0 B | 0 B | 0 B | 0 B | — |
| 029-streaming-c1-full-r3 | `spans` | 20 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 030-streaming-c8-empty-r1 | `runs` | 400 | 0 B | 328 KiB | 0 B | 328 KiB | ran |
| 030-streaming-c8-empty-r1 | `run_events` | 9600 | 3.55 MiB | 400 KiB | 0 B | 3.95 MiB | ran |
| 030-streaming-c8-empty-r1 | `run_inputs` | 400 | 496 KiB | 56 KiB | 0 B | 552 KiB | — |
| 030-streaming-c8-empty-r1 | `tool_invocations` | 400 | 560 KiB | 80 KiB | 0 B | 640 KiB | — |
| 030-streaming-c8-empty-r1 | `traces` | 39 | 0 B | 0 B | 0 B | 0 B | — |
| 030-streaming-c8-empty-r1 | `spans` | 195 | 104 KiB | 16 KiB | 0 B | 120 KiB | — |
| 031-streaming-c8-empty-r2 | `runs` | 392 | 16 KiB | 312 KiB | 0 B | 328 KiB | ran |
| 031-streaming-c8-empty-r2 | `run_events` | 9408 | 3.52 MiB | 392 KiB | 0 B | 3.9 MiB | ran |
| 031-streaming-c8-empty-r2 | `run_inputs` | 392 | 472 KiB | 56 KiB | 0 B | 528 KiB | — |
| 031-streaming-c8-empty-r2 | `tool_invocations` | 392 | 544 KiB | 80 KiB | 0 B | 624 KiB | — |
| 031-streaming-c8-empty-r2 | `traces` | 35 | 0 B | 0 B | 0 B | 0 B | — |
| 031-streaming-c8-empty-r2 | `spans` | 175 | 112 KiB | 0 B | 0 B | 112 KiB | — |
| 032-streaming-c8-empty-r3 | `runs` | 400 | 8 KiB | 320 KiB | 0 B | 328 KiB | ran |
| 032-streaming-c8-empty-r3 | `run_events` | 9600 | 3.55 MiB | 400 KiB | 0 B | 3.94 MiB | ran |
| 032-streaming-c8-empty-r3 | `run_inputs` | 400 | 472 KiB | 56 KiB | 0 B | 528 KiB | — |
| 032-streaming-c8-empty-r3 | `tool_invocations` | 400 | 552 KiB | 80 KiB | 0 B | 632 KiB | — |
| 032-streaming-c8-empty-r3 | `traces` | 41 | 0 B | 0 B | 0 B | 0 B | — |
| 032-streaming-c8-empty-r3 | `spans` | 205 | 104 KiB | 16 KiB | 0 B | 120 KiB | — |
| 033-streaming-c8-full-r1 | `runs` | 400 | 88 KiB | 288 KiB | 0 B | 376 KiB | — |
| 033-streaming-c8-full-r1 | `run_events` | 9600 | 3.54 MiB | 648 KiB | 0 B | 4.17 MiB | — |
| 033-streaming-c8-full-r1 | `run_inputs` | 400 | 504 KiB | 56 KiB | 0 B | 560 KiB | — |
| 033-streaming-c8-full-r1 | `tool_invocations` | 400 | 560 KiB | 80 KiB | 0 B | 640 KiB | — |
| 033-streaming-c8-full-r1 | `traces` | 42 | 0 B | 0 B | 0 B | 0 B | — |
| 033-streaming-c8-full-r1 | `spans` | 210 | 128 KiB | 16 KiB | 0 B | 144 KiB | — |
| 034-streaming-c8-full-r2 | `runs` | 400 | 72 KiB | 272 KiB | 0 B | 344 KiB | — |
| 034-streaming-c8-full-r2 | `run_events` | 9600 | 3.56 MiB | 648 KiB | 0 B | 4.2 MiB | — |
| 034-streaming-c8-full-r2 | `run_inputs` | 400 | 512 KiB | 56 KiB | 0 B | 568 KiB | — |
| 034-streaming-c8-full-r2 | `tool_invocations` | 400 | 568 KiB | 80 KiB | 0 B | 648 KiB | — |
| 034-streaming-c8-full-r2 | `traces` | 38 | 0 B | 0 B | 0 B | 0 B | — |
| 034-streaming-c8-full-r2 | `spans` | 190 | 104 KiB | 16 KiB | 0 B | 120 KiB | — |
| 035-streaming-c8-full-r3 | `runs` | 400 | 72 KiB | 288 KiB | 0 B | 360 KiB | — |
| 035-streaming-c8-full-r3 | `run_events` | 9600 | 3.57 MiB | 648 KiB | 0 B | 4.2 MiB | — |
| 035-streaming-c8-full-r3 | `run_inputs` | 400 | 464 KiB | 56 KiB | 0 B | 520 KiB | — |
| 035-streaming-c8-full-r3 | `tool_invocations` | 400 | 560 KiB | 80 KiB | 0 B | 640 KiB | — |
| 035-streaming-c8-full-r3 | `traces` | 40 | 0 B | 0 B | 0 B | 0 B | — |
| 035-streaming-c8-full-r3 | `spans` | 200 | 112 KiB | 16 KiB | 0 B | 128 KiB | — |
| 036-streaming-c32-empty-r1 | `runs` | 1728 | 320 KiB | 1.25 MiB | 0 B | 1.56 MiB | ran |
| 036-streaming-c32-empty-r1 | `run_events` | 41472 | 15.34 MiB | 2.2 MiB | 0 B | 17.55 MiB | ran |
| 036-streaming-c32-empty-r1 | `run_inputs` | 1728 | 1.98 MiB | 184 KiB | 0 B | 2.16 MiB | ran |
| 036-streaming-c32-empty-r1 | `tool_invocations` | 1728 | 2.26 MiB | 240 KiB | 0 B | 2.49 MiB | ran |
| 036-streaming-c32-empty-r1 | `traces` | 194 | 200 KiB | 24 KiB | 0 B | 224 KiB | — |
| 036-streaming-c32-empty-r1 | `spans` | 970 | 536 KiB | 72 KiB | 0 B | 608 KiB | ran |
| 037-streaming-c32-empty-r2 | `runs` | 1728 | 280 KiB | 1.16 MiB | 0 B | 1.44 MiB | ran |
| 037-streaming-c32-empty-r2 | `run_events` | 41472 | 15.41 MiB | 2.19 MiB | 0 B | 17.59 MiB | ran |
| 037-streaming-c32-empty-r2 | `run_inputs` | 1728 | 2.02 MiB | 184 KiB | 0 B | 2.2 MiB | ran |
| 037-streaming-c32-empty-r2 | `tool_invocations` | 1728 | 2.37 MiB | 248 KiB | 0 B | 2.61 MiB | ran |
| 037-streaming-c32-empty-r2 | `traces` | 191 | 248 KiB | 24 KiB | 0 B | 272 KiB | — |
| 037-streaming-c32-empty-r2 | `spans` | 955 | 520 KiB | 72 KiB | 0 B | 592 KiB | ran |
| 038-streaming-c32-empty-r3 | `runs` | 1770 | 344 KiB | 1.21 MiB | 0 B | 1.55 MiB | ran |
| 038-streaming-c32-empty-r3 | `run_events` | 42480 | 15.76 MiB | 2.24 MiB | 0 B | 18 MiB | ran |
| 038-streaming-c32-empty-r3 | `run_inputs` | 1770 | 2.04 MiB | 184 KiB | 0 B | 2.22 MiB | — |
| 038-streaming-c32-empty-r3 | `tool_invocations` | 1770 | 2.38 MiB | 248 KiB | 0 B | 2.62 MiB | — |
| 038-streaming-c32-empty-r3 | `traces` | 180 | 224 KiB | 24 KiB | 0 B | 248 KiB | — |
| 038-streaming-c32-empty-r3 | `spans` | 900 | 528 KiB | 88 KiB | 0 B | 616 KiB | — |
| 039-streaming-c32-full-r1 | `runs` | 1760 | 312 KiB | 1.3 MiB | 0 B | 1.61 MiB | — |
| 039-streaming-c32-full-r1 | `run_events` | 42240 | 15.68 MiB | 2.22 MiB | 0 B | 17.9 MiB | — |
| 039-streaming-c32-full-r1 | `run_inputs` | 1760 | 2.08 MiB | 184 KiB | 0 B | 2.26 MiB | — |
| 039-streaming-c32-full-r1 | `tool_invocations` | 1760 | 2.4 MiB | 248 KiB | 0 B | 2.64 MiB | — |
| 039-streaming-c32-full-r1 | `traces` | 189 | 296 KiB | 24 KiB | 0 B | 320 KiB | — |
| 039-streaming-c32-full-r1 | `spans` | 945 | 568 KiB | 80 KiB | 0 B | 648 KiB | — |
| 040-streaming-c32-full-r2 | `runs` | 1792 | 216 KiB | 1.35 MiB | 0 B | 1.56 MiB | ran |
| 040-streaming-c32-full-r2 | `run_events` | 43008 | 15.98 MiB | 2.2 MiB | 0 B | 18.19 MiB | ran |
| 040-streaming-c32-full-r2 | `run_inputs` | 1792 | 1.88 MiB | 184 KiB | 0 B | 2.05 MiB | ran |
| 040-streaming-c32-full-r2 | `tool_invocations` | 1792 | 2.19 MiB | 248 KiB | 0 B | 2.43 MiB | ran |
| 040-streaming-c32-full-r2 | `traces` | 158 | 240 KiB | 24 KiB | 0 B | 264 KiB | — |
| 040-streaming-c32-full-r2 | `spans` | 790 | 464 KiB | 64 KiB | 0 B | 528 KiB | — |
| 041-streaming-c32-full-r3 | `runs` | 1780 | 224 KiB | 1.35 MiB | 0 B | 1.57 MiB | ran |
| 041-streaming-c32-full-r3 | `run_events` | 42720 | 15.87 MiB | 2.16 MiB | 0 B | 18.02 MiB | ran |
| 041-streaming-c32-full-r3 | `run_inputs` | 1780 | 2.05 MiB | 184 KiB | 0 B | 2.23 MiB | ran |
| 041-streaming-c32-full-r3 | `tool_invocations` | 1780 | 2.3 MiB | 248 KiB | 0 B | 2.55 MiB | ran |
| 041-streaming-c32-full-r3 | `traces` | 175 | 240 KiB | 24 KiB | 0 B | 264 KiB | — |
| 041-streaming-c32-full-r3 | `spans` | 875 | 504 KiB | 88 KiB | 0 B | 592 KiB | ran |
| 042-streaming-c64-empty-r1 | `runs` | 3599 | 520 KiB | 2.62 MiB | 0 B | 3.13 MiB | ran |
| 042-streaming-c64-empty-r1 | `run_events` | 86376 | 31.52 MiB | 4.56 MiB | 0 B | 36.09 MiB | ran |
| 042-streaming-c64-empty-r1 | `run_inputs` | 3599 | 4.24 MiB | 368 KiB | 0 B | 4.6 MiB | ran |
| 042-streaming-c64-empty-r1 | `tool_invocations` | 3599 | 4.88 MiB | 512 KiB | 0 B | 5.38 MiB | ran |
| 042-streaming-c64-empty-r1 | `traces` | 375 | 328 KiB | 72 KiB | 0 B | 400 KiB | — |
| 042-streaming-c64-empty-r1 | `spans` | 1875 | 1.02 MiB | 120 KiB | 0 B | 1.13 MiB | ran |
| 043-streaming-c64-empty-r2 | `runs` | 3594 | 520 KiB | 2.54 MiB | 0 B | 3.05 MiB | ran |
| 043-streaming-c64-empty-r2 | `run_events` | 86256 | 32 MiB | 4.71 MiB | 0 B | 36.71 MiB | ran |
| 043-streaming-c64-empty-r2 | `run_inputs` | 3594 | 4.16 MiB | 368 KiB | 0 B | 4.52 MiB | — |
| 043-streaming-c64-empty-r2 | `tool_invocations` | 3594 | 4.87 MiB | 512 KiB | 0 B | 5.37 MiB | — |
| 043-streaming-c64-empty-r2 | `traces` | 358 | 384 KiB | 72 KiB | 0 B | 456 KiB | — |
| 043-streaming-c64-empty-r2 | `spans` | 1790 | 1.03 MiB | 120 KiB | 0 B | 1.15 MiB | — |
| 044-streaming-c64-empty-r3 | `runs` | 3525 | 520 KiB | 2.55 MiB | 0 B | 3.05 MiB | ran |
| 044-streaming-c64-empty-r3 | `run_events` | 84600 | 31.49 MiB | 4.6 MiB | 0 B | 36.09 MiB | ran |
| 044-streaming-c64-empty-r3 | `run_inputs` | 3525 | 4.11 MiB | 360 KiB | 0 B | 4.46 MiB | — |
| 044-streaming-c64-empty-r3 | `tool_invocations` | 3525 | 4.74 MiB | 488 KiB | 0 B | 5.22 MiB | — |
| 044-streaming-c64-empty-r3 | `traces` | 336 | 344 KiB | 72 KiB | 0 B | 416 KiB | — |
| 044-streaming-c64-empty-r3 | `spans` | 1680 | 992 KiB | 112 KiB | 0 B | 1.08 MiB | — |
| 045-streaming-c64-full-r1 | `runs` | 3520 | 824 KiB | 2.77 MiB | 0 B | 3.58 MiB | — |
| 045-streaming-c64-full-r1 | `run_events` | 84480 | 31.44 MiB | 4.3 MiB | 0 B | 35.73 MiB | — |
| 045-streaming-c64-full-r1 | `run_inputs` | 3520 | 3.76 MiB | 360 KiB | 0 B | 4.11 MiB | ran |
| 045-streaming-c64-full-r1 | `tool_invocations` | 3520 | 8 KiB | 488 KiB | 0 B | 496 KiB | ran |
| 045-streaming-c64-full-r1 | `traces` | 333 | 160 KiB | 72 KiB | 0 B | 232 KiB | — |
| 045-streaming-c64-full-r1 | `spans` | 1665 | 1000 KiB | 112 KiB | 0 B | 1.09 MiB | — |
| 046-streaming-c64-full-r2 | `runs` | 3522 | 536 KiB | 2.78 MiB | 0 B | 3.3 MiB | ran |
| 046-streaming-c64-full-r2 | `run_events` | 84528 | 32.29 MiB | 4.34 MiB | 0 B | 36.63 MiB | ran |
| 046-streaming-c64-full-r2 | `run_inputs` | 3522 | 3.9 MiB | 360 KiB | 0 B | 4.25 MiB | ran |
| 046-streaming-c64-full-r2 | `tool_invocations` | 3522 | 4.53 MiB | 488 KiB | 0 B | 5.01 MiB | ran |
| 046-streaming-c64-full-r2 | `traces` | 385 | 312 KiB | 72 KiB | 0 B | 384 KiB | — |
| 046-streaming-c64-full-r2 | `spans` | 1925 | 1.05 MiB | 128 KiB | 0 B | 1.17 MiB | ran |
| 047-streaming-c64-full-r3 | `runs` | 3584 | 824 KiB | 2.81 MiB | 0 B | 3.62 MiB | ran |
| 047-streaming-c64-full-r3 | `run_events` | 86016 | 30.84 MiB | 4.59 MiB | 0 B | 35.44 MiB | ran |
| 047-streaming-c64-full-r3 | `run_inputs` | 3584 | 4.07 MiB | 368 KiB | 0 B | 4.43 MiB | ran |
| 047-streaming-c64-full-r3 | `tool_invocations` | 3584 | 4.63 MiB | 496 KiB | 0 B | 5.11 MiB | ran |
| 047-streaming-c64-full-r3 | `traces` | 394 | 352 KiB | 72 KiB | 0 B | 424 KiB | — |
| 047-streaming-c64-full-r3 | `spans` | 1970 | 1.08 MiB | 144 KiB | 0 B | 1.22 MiB | ran |
| 048-queued-c1-empty-r1 | `runs` | 47 | 56 KiB | 0 B | 0 B | 56 KiB | ran |
| 048-queued-c1-empty-r1 | `run_events` | 282 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 048-queued-c1-empty-r1 | `run_inputs` | 47 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 048-queued-c1-empty-r1 | `tool_invocations` | 47 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 048-queued-c1-empty-r1 | `jobs` | 47 | 72 KiB | 0 B | 0 B | 72 KiB | ran |
| 048-queued-c1-empty-r1 | `traces` | 10 | 0 B | 0 B | 0 B | 0 B | — |
| 048-queued-c1-empty-r1 | `spans` | 50 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 049-queued-c1-empty-r2 | `runs` | 46 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 049-queued-c1-empty-r2 | `run_events` | 276 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 049-queued-c1-empty-r2 | `run_inputs` | 46 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 049-queued-c1-empty-r2 | `tool_invocations` | 46 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 049-queued-c1-empty-r2 | `jobs` | 46 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 049-queued-c1-empty-r2 | `traces` | 5 | 8 KiB | 24 KiB | 0 B | 32 KiB | — |
| 049-queued-c1-empty-r2 | `spans` | 25 | 48 KiB | 16 KiB | 0 B | 64 KiB | — |
| 050-queued-c1-empty-r3 | `runs` | 47 | 48 KiB | 0 B | 0 B | 48 KiB | ran |
| 050-queued-c1-empty-r3 | `run_events` | 282 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 050-queued-c1-empty-r3 | `run_inputs` | 47 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 050-queued-c1-empty-r3 | `tool_invocations` | 47 | 72 KiB | 0 B | 0 B | 72 KiB | — |
| 050-queued-c1-empty-r3 | `jobs` | 47 | 72 KiB | 0 B | 0 B | 72 KiB | ran |
| 050-queued-c1-empty-r3 | `traces` | 7 | 0 B | 0 B | 0 B | 0 B | — |
| 050-queued-c1-empty-r3 | `spans` | 35 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 051-queued-c1-full-r1 | `runs` | 46 | 0 B | 32 KiB | 0 B | 32 KiB | — |
| 051-queued-c1-full-r1 | `run_events` | 276 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 051-queued-c1-full-r1 | `run_inputs` | 46 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 051-queued-c1-full-r1 | `tool_invocations` | 46 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 051-queued-c1-full-r1 | `jobs` | 46 | 64 KiB | 0 B | 0 B | 64 KiB | ran |
| 051-queued-c1-full-r1 | `traces` | 3 | 0 B | 0 B | 0 B | 0 B | — |
| 051-queued-c1-full-r1 | `spans` | 15 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 052-queued-c1-full-r2 | `runs` | 46 | 0 B | 32 KiB | 0 B | 32 KiB | — |
| 052-queued-c1-full-r2 | `run_events` | 276 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 052-queued-c1-full-r2 | `run_inputs` | 46 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 052-queued-c1-full-r2 | `tool_invocations` | 46 | 64 KiB | 0 B | 0 B | 64 KiB | — |
| 052-queued-c1-full-r2 | `jobs` | 46 | 64 KiB | 0 B | 0 B | 64 KiB | ran |
| 052-queued-c1-full-r2 | `traces` | 3 | 0 B | 0 B | 0 B | 0 B | — |
| 052-queued-c1-full-r2 | `spans` | 15 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 053-queued-c1-full-r3 | `runs` | 46 | 0 B | 32 KiB | 0 B | 32 KiB | — |
| 053-queued-c1-full-r3 | `run_events` | 276 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 053-queued-c1-full-r3 | `run_inputs` | 46 | 48 KiB | 0 B | 0 B | 48 KiB | — |
| 053-queued-c1-full-r3 | `tool_invocations` | 46 | 56 KiB | 0 B | 0 B | 56 KiB | — |
| 053-queued-c1-full-r3 | `jobs` | 46 | 80 KiB | 0 B | 0 B | 80 KiB | ran |
| 053-queued-c1-full-r3 | `traces` | 4 | 0 B | 0 B | 0 B | 0 B | — |
| 053-queued-c1-full-r3 | `spans` | 20 | 40 KiB | 0 B | 0 B | 40 KiB | — |
| 054-queued-c8-empty-r1 | `runs` | 368 | 88 KiB | 368 KiB | 0 B | 456 KiB | ran |
| 054-queued-c8-empty-r1 | `run_events` | 2208 | 1.14 MiB | 88 KiB | 0 B | 1.23 MiB | — |
| 054-queued-c8-empty-r1 | `run_inputs` | 368 | 464 KiB | 40 KiB | 0 B | 504 KiB | — |
| 054-queued-c8-empty-r1 | `tool_invocations` | 368 | 544 KiB | 72 KiB | 0 B | 616 KiB | — |
| 054-queued-c8-empty-r1 | `jobs` | 368 | 544 KiB | 136 KiB | 0 B | 680 KiB | ran |
| 054-queued-c8-empty-r1 | `traces` | 44 | 0 B | 0 B | 0 B | 0 B | — |
| 054-queued-c8-empty-r1 | `spans` | 220 | 136 KiB | 16 KiB | 0 B | 152 KiB | — |
| 055-queued-c8-empty-r2 | `runs` | 368 | 136 KiB | 376 KiB | 0 B | 512 KiB | ran |
| 055-queued-c8-empty-r2 | `run_events` | 2208 | 1.15 MiB | 88 KiB | 0 B | 1.23 MiB | — |
| 055-queued-c8-empty-r2 | `run_inputs` | 368 | 496 KiB | 40 KiB | 0 B | 536 KiB | — |
| 055-queued-c8-empty-r2 | `tool_invocations` | 368 | 536 KiB | 72 KiB | 0 B | 608 KiB | — |
| 055-queued-c8-empty-r2 | `jobs` | 368 | 544 KiB | 136 KiB | 0 B | 680 KiB | ran |
| 055-queued-c8-empty-r2 | `traces` | 30 | 0 B | 0 B | 0 B | 0 B | — |
| 055-queued-c8-empty-r2 | `spans` | 150 | 96 KiB | 0 B | 0 B | 96 KiB | — |
| 056-queued-c8-empty-r3 | `runs` | 368 | 120 KiB | 328 KiB | 0 B | 448 KiB | ran |
| 056-queued-c8-empty-r3 | `run_events` | 2208 | 1.09 MiB | 88 KiB | 0 B | 1.17 MiB | ran |
| 056-queued-c8-empty-r3 | `run_inputs` | 368 | 464 KiB | 48 KiB | 0 B | 512 KiB | — |
| 056-queued-c8-empty-r3 | `tool_invocations` | 368 | 536 KiB | 72 KiB | 0 B | 608 KiB | — |
| 056-queued-c8-empty-r3 | `jobs` | 368 | 520 KiB | 136 KiB | 0 B | 656 KiB | ran |
| 056-queued-c8-empty-r3 | `traces` | 42 | 0 B | 0 B | 0 B | 0 B | — |
| 056-queued-c8-empty-r3 | `spans` | 210 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |
| 057-queued-c8-full-r1 | `runs` | 368 | 88 KiB | 392 KiB | 0 B | 480 KiB | — |
| 057-queued-c8-full-r1 | `run_events` | 2208 | 1.08 MiB | 152 KiB | 0 B | 1.23 MiB | — |
| 057-queued-c8-full-r1 | `run_inputs` | 368 | 472 KiB | 40 KiB | 0 B | 512 KiB | — |
| 057-queued-c8-full-r1 | `tool_invocations` | 368 | 520 KiB | 72 KiB | 0 B | 592 KiB | — |
| 057-queued-c8-full-r1 | `jobs` | 368 | 528 KiB | 152 KiB | 0 B | 680 KiB | ran |
| 057-queued-c8-full-r1 | `traces` | 37 | 0 B | 0 B | 0 B | 0 B | — |
| 057-queued-c8-full-r1 | `spans` | 185 | 112 KiB | 0 B | 0 B | 112 KiB | — |
| 058-queued-c8-full-r2 | `runs` | 368 | 136 KiB | 368 KiB | 0 B | 504 KiB | — |
| 058-queued-c8-full-r2 | `run_events` | 2208 | 1.1 MiB | 152 KiB | 0 B | 1.25 MiB | — |
| 058-queued-c8-full-r2 | `run_inputs` | 368 | 464 KiB | 48 KiB | 0 B | 512 KiB | — |
| 058-queued-c8-full-r2 | `tool_invocations` | 368 | 544 KiB | 72 KiB | 0 B | 616 KiB | — |
| 058-queued-c8-full-r2 | `jobs` | 368 | 544 KiB | 136 KiB | 0 B | 680 KiB | ran |
| 058-queued-c8-full-r2 | `traces` | 40 | 0 B | 0 B | 0 B | 0 B | — |
| 058-queued-c8-full-r2 | `spans` | 200 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |
| 059-queued-c8-full-r3 | `runs` | 368 | 128 KiB | 368 KiB | 0 B | 496 KiB | — |
| 059-queued-c8-full-r3 | `run_events` | 2208 | 1.1 MiB | 152 KiB | 0 B | 1.25 MiB | — |
| 059-queued-c8-full-r3 | `run_inputs` | 368 | 472 KiB | 40 KiB | 0 B | 512 KiB | — |
| 059-queued-c8-full-r3 | `tool_invocations` | 368 | 544 KiB | 72 KiB | 0 B | 616 KiB | — |
| 059-queued-c8-full-r3 | `jobs` | 368 | 536 KiB | 136 KiB | 0 B | 672 KiB | ran |
| 059-queued-c8-full-r3 | `traces` | 34 | 0 B | 0 B | 0 B | 0 B | — |
| 059-queued-c8-full-r3 | `spans` | 170 | 96 KiB | 0 B | 0 B | 96 KiB | — |
| 060-queued-c32-empty-r1 | `runs` | 464 | 272 KiB | 376 KiB | 0 B | 648 KiB | ran |
| 060-queued-c32-empty-r1 | `run_events` | 2784 | 1.41 MiB | 120 KiB | 0 B | 1.52 MiB | ran |
| 060-queued-c32-empty-r1 | `run_inputs` | 464 | 688 KiB | 64 KiB | 0 B | 752 KiB | — |
| 060-queued-c32-empty-r1 | `tool_invocations` | 464 | 784 KiB | 96 KiB | 0 B | 880 KiB | — |
| 060-queued-c32-empty-r1 | `jobs` | 464 | 824 KiB | 168 KiB | 0 B | 992 KiB | ran |
| 060-queued-c32-empty-r1 | `traces` | 41 | 0 B | 0 B | 0 B | 0 B | — |
| 060-queued-c32-empty-r1 | `spans` | 205 | 144 KiB | 16 KiB | 0 B | 160 KiB | — |
| 061-queued-c32-empty-r2 | `runs` | 464 | 240 KiB | 384 KiB | 0 B | 624 KiB | ran |
| 061-queued-c32-empty-r2 | `run_events` | 2784 | 1.12 MiB | 112 KiB | 0 B | 1.23 MiB | ran |
| 061-queued-c32-empty-r2 | `run_inputs` | 464 | 688 KiB | 64 KiB | 0 B | 752 KiB | — |
| 061-queued-c32-empty-r2 | `tool_invocations` | 464 | 768 KiB | 96 KiB | 0 B | 864 KiB | — |
| 061-queued-c32-empty-r2 | `jobs` | 464 | 720 KiB | 152 KiB | 0 B | 872 KiB | ran |
| 061-queued-c32-empty-r2 | `traces` | 48 | 0 B | 0 B | 0 B | 0 B | — |
| 061-queued-c32-empty-r2 | `spans` | 240 | 144 KiB | 32 KiB | 0 B | 176 KiB | — |
| 062-queued-c32-empty-r3 | `runs` | 464 | 0 B | 368 KiB | 0 B | 368 KiB | ran |
| 062-queued-c32-empty-r3 | `run_events` | 2784 | 1.39 MiB | 112 KiB | 0 B | 1.5 MiB | ran |
| 062-queued-c32-empty-r3 | `run_inputs` | 464 | 664 KiB | 64 KiB | 0 B | 728 KiB | — |
| 062-queued-c32-empty-r3 | `tool_invocations` | 464 | 736 KiB | 96 KiB | 0 B | 832 KiB | — |
| 062-queued-c32-empty-r3 | `jobs` | 464 | 848 KiB | 160 KiB | 0 B | 1008 KiB | ran |
| 062-queued-c32-empty-r3 | `traces` | 56 | 0 B | 0 B | 0 B | 0 B | — |
| 062-queued-c32-empty-r3 | `spans` | 280 | 152 KiB | 40 KiB | 0 B | 192 KiB | — |
| 063-queued-c32-full-r1 | `runs` | 464 | 360 KiB | 448 KiB | 0 B | 808 KiB | — |
| 063-queued-c32-full-r1 | `run_events` | 2784 | 1.36 MiB | 192 KiB | 0 B | 1.55 MiB | — |
| 063-queued-c32-full-r1 | `run_inputs` | 464 | 712 KiB | 64 KiB | 0 B | 776 KiB | — |
| 063-queued-c32-full-r1 | `tool_invocations` | 464 | 768 KiB | 96 KiB | 0 B | 864 KiB | — |
| 063-queued-c32-full-r1 | `jobs` | 464 | 720 KiB | 152 KiB | 0 B | 872 KiB | ran |
| 063-queued-c32-full-r1 | `traces` | 46 | 8 KiB | 0 B | 0 B | 8 KiB | — |
| 063-queued-c32-full-r1 | `spans` | 230 | 152 KiB | 40 KiB | 0 B | 192 KiB | — |
| 064-queued-c32-full-r2 | `runs` | 464 | 400 KiB | 448 KiB | 0 B | 848 KiB | — |
| 064-queued-c32-full-r2 | `run_events` | 2784 | 1.39 MiB | 192 KiB | 0 B | 1.58 MiB | — |
| 064-queued-c32-full-r2 | `run_inputs` | 464 | 648 KiB | 64 KiB | 0 B | 712 KiB | — |
| 064-queued-c32-full-r2 | `tool_invocations` | 464 | 736 KiB | 96 KiB | 0 B | 832 KiB | — |
| 064-queued-c32-full-r2 | `jobs` | 464 | 664 KiB | 152 KiB | 0 B | 816 KiB | ran |
| 064-queued-c32-full-r2 | `traces` | 51 | 0 B | 0 B | 0 B | 0 B | — |
| 064-queued-c32-full-r2 | `spans` | 255 | 152 KiB | 32 KiB | 0 B | 184 KiB | — |
| 065-queued-c32-full-r3 | `runs` | 464 | 352 KiB | 440 KiB | 0 B | 792 KiB | — |
| 065-queued-c32-full-r3 | `run_events` | 2784 | 1.38 MiB | 192 KiB | 0 B | 1.56 MiB | — |
| 065-queued-c32-full-r3 | `run_inputs` | 464 | 680 KiB | 64 KiB | 0 B | 744 KiB | — |
| 065-queued-c32-full-r3 | `tool_invocations` | 464 | 752 KiB | 96 KiB | 0 B | 848 KiB | — |
| 065-queued-c32-full-r3 | `jobs` | 464 | 728 KiB | 152 KiB | 0 B | 880 KiB | ran |
| 065-queued-c32-full-r3 | `traces` | 39 | 0 B | 0 B | 0 B | 0 B | — |
| 065-queued-c32-full-r3 | `spans` | 195 | 120 KiB | 16 KiB | 0 B | 136 KiB | — |
| 066-queued-c64-empty-r1 | `runs` | 496 | 256 KiB | 424 KiB | 0 B | 680 KiB | ran |
| 066-queued-c64-empty-r1 | `run_events` | 2976 | 904 KiB | 120 KiB | 0 B | 1 MiB | ran |
| 066-queued-c64-empty-r1 | `run_inputs` | 496 | 936 KiB | 64 KiB | 0 B | 1000 KiB | — |
| 066-queued-c64-empty-r1 | `tool_invocations` | 496 | 928 KiB | 104 KiB | 0 B | 1.01 MiB | — |
| 066-queued-c64-empty-r1 | `jobs` | 496 | 688 KiB | 208 KiB | 0 B | 896 KiB | ran |
| 066-queued-c64-empty-r1 | `traces` | 37 | 0 B | 0 B | 0 B | 0 B | — |
| 066-queued-c64-empty-r1 | `spans` | 185 | 112 KiB | 16 KiB | 0 B | 128 KiB | — |
| 067-queued-c64-empty-r2 | `runs` | 496 | 448 KiB | 360 KiB | 0 B | 808 KiB | ran |
| 067-queued-c64-empty-r2 | `run_events` | 2976 | 1.12 MiB | 120 KiB | 0 B | 1.23 MiB | ran |
| 067-queued-c64-empty-r2 | `run_inputs` | 496 | 872 KiB | 64 KiB | 0 B | 936 KiB | — |
| 067-queued-c64-empty-r2 | `tool_invocations` | 496 | 952 KiB | 104 KiB | 0 B | 1.03 MiB | — |
| 067-queued-c64-empty-r2 | `jobs` | 496 | 776 KiB | 168 KiB | 0 B | 944 KiB | ran |
| 067-queued-c64-empty-r2 | `traces` | 47 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 067-queued-c64-empty-r2 | `spans` | 235 | 120 KiB | 40 KiB | 0 B | 160 KiB | — |
| 068-queued-c64-empty-r3 | `runs` | 496 | 456 KiB | 368 KiB | 0 B | 824 KiB | ran |
| 068-queued-c64-empty-r3 | `run_events` | 2976 | 1.01 MiB | 120 KiB | 0 B | 1.13 MiB | ran |
| 068-queued-c64-empty-r3 | `run_inputs` | 496 | 896 KiB | 64 KiB | 0 B | 960 KiB | — |
| 068-queued-c64-empty-r3 | `tool_invocations` | 496 | 1016 KiB | 104 KiB | 0 B | 1.09 MiB | — |
| 068-queued-c64-empty-r3 | `jobs` | 496 | 728 KiB | 192 KiB | 0 B | 920 KiB | ran |
| 068-queued-c64-empty-r3 | `traces` | 45 | 0 B | 0 B | 0 B | 0 B | — |
| 068-queued-c64-empty-r3 | `spans` | 225 | 128 KiB | 32 KiB | 0 B | 160 KiB | — |
| 069-queued-c64-full-r1 | `runs` | 480 | 512 KiB | 472 KiB | 0 B | 984 KiB | — |
| 069-queued-c64-full-r1 | `run_events` | 2880 | 1.46 MiB | 200 KiB | 0 B | 1.66 MiB | — |
| 069-queued-c64-full-r1 | `run_inputs` | 480 | 840 KiB | 64 KiB | 0 B | 904 KiB | — |
| 069-queued-c64-full-r1 | `tool_invocations` | 480 | 920 KiB | 104 KiB | 0 B | 1 MiB | — |
| 069-queued-c64-full-r1 | `jobs` | 480 | 720 KiB | 200 KiB | 0 B | 920 KiB | ran |
| 069-queued-c64-full-r1 | `traces` | 44 | 0 B | 0 B | 0 B | 0 B | — |
| 069-queued-c64-full-r1 | `spans` | 220 | 128 KiB | 32 KiB | 0 B | 160 KiB | — |
| 070-queued-c64-full-r2 | `runs` | 488 | 368 KiB | 472 KiB | 0 B | 840 KiB | — |
| 070-queued-c64-full-r2 | `run_events` | 2928 | 1.47 MiB | 200 KiB | 0 B | 1.66 MiB | — |
| 070-queued-c64-full-r2 | `run_inputs` | 488 | 848 KiB | 64 KiB | 0 B | 912 KiB | — |
| 070-queued-c64-full-r2 | `tool_invocations` | 488 | 952 KiB | 104 KiB | 0 B | 1.03 MiB | — |
| 070-queued-c64-full-r2 | `jobs` | 488 | 800 KiB | 160 KiB | 0 B | 960 KiB | ran |
| 070-queued-c64-full-r2 | `traces` | 51 | 32 KiB | 0 B | 0 B | 32 KiB | — |
| 070-queued-c64-full-r2 | `spans` | 255 | 128 KiB | 40 KiB | 0 B | 168 KiB | — |
| 071-queued-c64-full-r3 | `runs` | 488 | 312 KiB | 480 KiB | 0 B | 792 KiB | — |
| 071-queued-c64-full-r3 | `run_events` | 2928 | 1.46 MiB | 200 KiB | 0 B | 1.66 MiB | — |
| 071-queued-c64-full-r3 | `run_inputs` | 488 | 928 KiB | 64 KiB | 0 B | 992 KiB | — |
| 071-queued-c64-full-r3 | `tool_invocations` | 488 | 968 KiB | 104 KiB | 0 B | 1.05 MiB | — |
| 071-queued-c64-full-r3 | `jobs` | 488 | 760 KiB | 192 KiB | 0 B | 952 KiB | ran |
| 071-queued-c64-full-r3 | `traces` | 48 | 0 B | 0 B | 0 B | 0 B | — |
| 071-queued-c64-full-r3 | `spans` | 240 | 136 KiB | 32 KiB | 0 B | 168 KiB | — |

## Telemetry coverage

These measurements were **unavailable** and are written as such, never as zero:

- `queue.wait`: no queued job recorded a start time

## First bottleneck

`queued` (empty database) stopped scaling between concurrency 8 and 32: throughput moved 5.93/s → 6.75/s (14 %) while p50 latency grew 1317.63 ms → 4456.82 ms (238 %). The extra offered load bought queue depth, not work. No request was refused, so this ceiling is invisible to a rejection count.

## What this report does not say

- Measured on one machine, one database and one configuration. This is not an SLA and not a guaranteed capacity.
- One machine, one database, one clock. Network partition, clock skew and inter-machine latency are out of scope, so nothing here says anything about multi-node behaviour.
- Autovacuum ran inside at least one window; those cells' byte growth is flagged and excluded from the averages. Their row counts remain usable.
