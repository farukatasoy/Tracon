# Kept measurements

One directory per profile, holding what a number needs in order to travel: the
`manifest.json` that says what was measured and under what conditions, the
`summary.json` the report was computed from, the `report.md` itself, and its
standalone charts.

Raw samples are **not** here. `requests.jsonl` and `resources.jsonl` run to tens
of megabytes for a thirty-minute soak; they stay under `artifacts/capacity/`,
which is not tracked. Everything in this directory is derived from them and can
be recomputed from a run directory with:

```bash
Tracon.CapacityDriver report --run artifacts/capacity/<run-id>
```

`nodes` measured Tracon packed from commit `d52f458c` (2026-10-29, version `0.0.0-capacity265.647aa02`, clean tree). Its
`report.md` was recomputed afterwards: the closing caveat names two processes on one machine (not "nothing about multi-node"), and
the processor time and peak memory of a two-host cell are the two hosts added together (the first version of the report read
only the first host). The cell results are untouched.

These are the runs the numbers in
`docs-site/src/content/docs/guides/production.md` come from. `sweep` and
`arrival` measured Tracon packed from commit `9a7cc145`; `workers` and `soak`
measured `466f63fe`, because two apparatus defects were fixed between them. The
**shipped source is byte identical across those commits** — `git diff
9a7cc145..466f63fe -- src/` is empty — so the two halves measure the same
Tracon. Each manifest names its own commit and package version; do not read one
commit off another run's manifest.

A manifest names the commit in the history its run was measured in, and the
package version carries the first seven characters of that identifier. A clone
of this repository does not hold those commits, so `public-commits.json` maps
each one to the commit here whose `src/` and `bench/` trees are byte identical
to it (the identifiers above are the mapped ones). The page and this file name
the mapped commit; the manifests and reports are measurement records and keep
what the run wrote. A new run is added to the map once its commit has reached
this repository.

| Profile | What it answers | Cells |
|---|---|---|
| `sweep` | How the three request paths behave from concurrency 1 to 64, against an empty and a 10 000-run database | 72, all complete |
| `arrival` | What happens when the send plan does not wait for the previous answer | 12, all complete |
| `workers` | How queued work spreads across 1, 2 and 4 leasing processes at constant offered load | 9, all complete |
| `soak` | Whether thirty minutes at a chosen load accumulates anything | 1, complete |
| `nodes` | What a second HTTP host, with its own leasing worker, costs and buys at constant offered load, on one database | 12, all complete |

> Every number in these files describes one machine, one PostgreSQL version and
> one configuration. None of it is an SLA or a guaranteed capacity, and none of
> it says anything about running Tracon on more than one machine.
