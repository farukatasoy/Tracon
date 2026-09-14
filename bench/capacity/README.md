# Capacity measurement (Phase 166)

What a real package consumer's HTTP and SQL path does under load, on one
machine, at one configuration.

> **This is a measurement, not a guarantee.** Every number produced here
> describes the machine, database and configuration named in its own
> `manifest.json`. It is not an SLA, not a guaranteed capacity, and it says
> nothing about multi-node behaviour: one machine, one database, one clock.

---

## Running it

```bash
python3 scripts/kapi.py kapasite --profil smoke    --surum 1.0.0-preview.N
python3 scripts/kapi.py kapasite --profil sweep    --surum 1.0.0-preview.N
python3 scripts/kapi.py kapasite --profil arrival  --surum 1.0.0-preview.N
python3 scripts/kapi.py kapasite --profil workers  --surum 1.0.0-preview.N
python3 scripts/kapi.py kapasite --profil soak     --surum 1.0.0-preview.N
```

`--surum` takes an **exact** version. A floating version (`*`, `*-*`) is
refused: a report that cannot say which bytes it measured is not evidence.

Requirements: Docker (the run starts its own PostgreSQL container and removes
it afterwards) and enough free disk for the profile's budget. Set
`TRACON_CAPACITY_CONNECTION` to point at an existing server instead — the run
then does not manage databases and refuses the profiles that need to.

`workers` is run **after** `sweep`: its fixed concurrency is chosen from the
steps `sweep` completed without accumulating a backlog, and the choice belongs
in the report.

### Running against an uncommitted tree

A dirty working tree has no provenance, so `dotnet pack` refuses it unless the
version carries `dirty`:

```bash
python3 scripts/kapi.py kapasite --profil smoke --surum 0.0.0-dirty.local
```

A dirty run is for developing the apparatus. Nothing measured under one may be
published.

---

## What is here

| Path | What it is |
|---|---|
| `Shared/` | The names and the synthetic payload both sides compute. **Linked** into every project, never copied |
| `Tracon.CapacityHost/` | The system under measurement. Consumes `Tracon` by `PackageReference` at one exact version. Four modes: `api`, `worker`, `seed`, `migrate` |
| `Tracon.CapacityDriver/` | A separate process that speaks only HTTP and read-only SQL. Runs one cell, or merges finished cells into a report |
| `Tracon.Capacity.Acceptance/` | Short checks against a real packed host and a real database. Run only by the `smoke` profile |
| `profiles/*.json` | The five load shapes |
| `Directory.Build.props`, `Directory.Packages.props` | Deliberately cut off from the repository's own MSBuild inheritance |

`tests/Tracon.Capacity.Tests` is the only capacity project the normal suite
discovers; it tests the driver's arithmetic at ordinary suite speed.

---

## Why it is built this way

**The host consumes a package, never the source tree.** A `ProjectReference`
into `src/` would make the report describe the working tree rather than what a
customer installs, and the assembly-load path and dependency graph a consumer
actually gets would go unmeasured. `scripts/capacity.py` packs at an exact
version, restores from an isolated feed with an empty cache, and the acceptance
suite asserts the restore's own record.

**The driver is a separate process.** An in-process test server cannot prove
anything about a socket. Host, driver and each worker are sampled separately by
pid, because telling a saturated server from a saturated client is the
difference between a capacity finding and a driver bug.

**The send plan is fixed before the window opens.** A closed-loop driver
silently lowers the load it claims to apply: a slow server makes a slow driver,
and the report then shows healthy latency at a load nobody offered. Requests
that come due with no in-flight slot are counted as `notSent`, never deferred.

**Every cell reconciles.** Tracon's own rule is that observability must not
break functionality — a store that refuses a write leaves the run green. That
is correct, and it is exactly why counting HTTP successes alone could report a
clean measurement over lost data.

**A measurement that could not be taken is never written as zero.** It is
listed as unavailable with its reason. A zero reads as "measured, and it was
nothing", which is a different and unfalsifiable claim.

---

## Reading a run

```
artifacts/capacity/<run-id>/
  manifest.json          environment, package hashes, effective settings, seed shape
  summary.json           one row per load point, repeats merged
  report.md              the same data as prose and tables
  charts/*.svg           standalone, no script and no external reference
  cells/<cell>/
    spec.json            what the cell was asked to do
    cell.json            what it did
    requests.jsonl       one line per request
    resources.jsonl      one line per process per sample
    executions/          which process served which run
```

Every cell ends `complete`, `incomplete/<reason>` or `invalid`.

- **`incomplete`** means the cell stopped short — a resource cap, the drain
  budget, an interrupt. Its numbers describe less than the whole window and the
  report says so. Higher steps on the same axis are not run after one.
- **`invalid`** means the measurement cannot be trusted: a counting error, lost
  data, tenant bleed, a missing mandatory measurement, or two workers running
  one job at once. **Slowness never makes a cell invalid** — that is a finding
  about the system, not about the measurement.

Percentiles use nearest rank over the retained samples and carry their sample
count. `⚠` marks one computed below its floor (p95: 100 samples, p99: 1000).
Repeats are merged by sample count, never by averaging percentiles.

### Write amplification

Byte growth comes from `pg_total_relation_size`, which counts bloat as well as
data. Three defences apply and all three are reported: each cell owns a fresh
database, the autovacuum state is read on both sides of the window, and the
closing reading is taken only after the drain. A cell where autovacuum ran is
flagged `storage/vacuum-interference` and its bytes stay out of every average —
its **row** counts remain usable, which is why rows and bytes always appear
together.

### The worker axis

`workers` holds the offered load constant and changes only the number of
leasing processes. The HTTP host's own in-process worker is turned off and the
driver **re-reads that at run time**: with it still leasing, "1 worker" would be
two and every point on the axis would shift, so the profile refuses to start.

Which process ran which job comes from the processes' own execution records —
the scheduler nulls `jobs.lease_owner` on completion, so the database cannot say
afterwards. An extra attempt is legitimate (`IJobHandler` is at-least-once); an
**overlapping** attempt is not and makes the cell invalid. When one worker takes
nearly every job the run reports "contention could not be measured" rather than
claiming a scaling result.

🚨 None of this says multiple nodes are supported. It is one machine, one
database and one clock; network partition, clock skew and inter-machine latency
are out of scope.

---

## What the apparatus refuses to do

- Publish a number without its manifest.
- Compare runs whose workload, configuration, dataset or environment differ.
- Turn a heavy profile into a CI gate. Only `smoke` runs in CI, and it checks
  correctness, not speed; no duration here is bound to any threshold.
- Take a connection string as a process argument. It travels only in the
  environment, and every artifact is scanned before it is kept.
