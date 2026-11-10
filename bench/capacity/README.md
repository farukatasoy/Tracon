# Capacity measurement

What a real package consumer's HTTP and SQL path does under load, on one
machine, at one configuration.

> **This is a measurement, not a guarantee.** Every number produced here
> describes the machine, database and configuration named in its own
> `manifest.json`. It is not an SLA, not a guaranteed capacity, and it says
> nothing about multi-node behaviour: one machine, one database, one clock. The `nodes` profile
> puts two HTTP hosts on that one machine and database; it says what a second host costs and buys there, nothing about
> network, clock skew or more than two hosts.

---

## Running it

```bash
python3 scripts/gate.py capacity --profile smoke    --package-version 1.0.0-preview.N
python3 scripts/gate.py capacity --profile sweep    --package-version 1.0.0-preview.N
python3 scripts/gate.py capacity --profile arrival  --package-version 1.0.0-preview.N
python3 scripts/gate.py capacity --profile workers  --package-version 1.0.0-preview.N
python3 scripts/gate.py capacity --profile soak     --package-version 1.0.0-preview.N
python3 scripts/gate.py capacity --profile nodes    --package-version 1.0.0-preview.N
python3 scripts/gate.py capacity --profile tenant-ramp --package-version 1.0.0-preview.N
```

`--package-version` takes an **exact** version. A floating version (`*`, `*-*`) is
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
python3 scripts/gate.py capacity --profile smoke --package-version 0.0.0-dirty.local
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
| `profiles/*.json` | The load shapes: six capacity profiles, the tenant ramp and its five-minute development form |
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

### The host axis

`nodes` holds the offered load constant and changes only the number of HTTP hosts: one, then two, each with its own
leasing worker, on one database. The driver sends each call to the next host in turn, so the read that follows a
request usually reaches the other host. Both hosts must lease (`Scheduling:RunWorker` on), or the cell is invalid: a
host that only serves would make "2 hosts" one host and a bystander. Every cell still reconciles what was sent with
what the store holds, so a run that one host wrote and the other lost shows as a missing run.

🚨 None of this says more than two nodes are supported. It is one machine, one
database and one clock; network partition, clock skew and inter-machine latency
are out of scope.

---

### The tenant axis

`tenant-ramp` does not ask how fast. It asks what the host process **keeps**: whether its working set, live managed
heap, thread, timer, descriptor, connection and metric-series counts grow with time, with runs, or with the number of
tenants.

The load is spread over more and more tenants (2, 20, 200, 2000, 10000 in the shipped profile). Each stage lets its new
tenants join with a few runs each, then holds steady load across every tenant that has joined, and the cell ends with an
idle stretch. The two kinds of growth are read apart on purpose:

- **Inside a plateau** the tenants are fixed, so a sustained rise follows time or runs. That is red by any reading.
- **Between plateaus** the rise is the cost of the tenants added. A rise in bytes is reported as a cost per tenant and is
  red only if that cost per tenant climbs from one step to the next. A rise in a *count* of threads, timers or descriptors
  is red at any size: none of them should follow the number of tenants.
- **The connection pool is the exception to that rule**, because a database client's pool opens and closes connections
  with the load and is never flat: in six recorded cells (26 plateaus) the median of a plateau sat between 9 and 15
  connections at 2 to 10 000 tenants with no relation to the tenants, the largest rise from one plateau to the next was 5
  (7 when the end of the plateau is read instead of the whole), and the readings inside a plateau spread by at most 7. It is
  judged as a *pooled* count:
  the level of a plateau is the median of the whole plateau, not of its last third; a rise from one plateau to the next
  counts only if it is larger than the pool's own breathing (8 connections, a value just above the largest seen, or the
  spread of the first plateau when that is wider) **and** the plateau's lowest window rose too, because a connection held for a tenant does not go back down when
  the load eases and one the pool opened for the load does; and a plateau whose median reaches the pool's limit (the
  host reports it, and it is written to `ramp.json`) is red at any size. A leak smaller than the breathing is below what
  this rule can see, and the floor on each step says so.
- **At idle** the live heap, measured after a forced compacting collection, minus the first plateau's, divided by the
  tenants added since, is what the host still holds per tenant when nothing is using it.

The host opens tenancy to any well-formed tenant id for this profile only (an empty allow list, the shape being
measured) and reports its own thread, timer, descriptor, heap and metric-series counts on `/capacity/diagnostics`; the
driver reads that once a second, forces a full collection every `collectIntervalSeconds`, and reads the host's
connection count from `pg_stat_activity`.

`footprint.md` (with `footprint.json` and the per-second `footprint.csv`) is written beside `cell.json`, and again by
`Tracon.CapacityDriver footprint --cell <cell directory>`. Every plateau row carries the **floor**: the smallest growth
that would have counted. A flat row is "no growth above the floor", and a short plateau has a high floor.
`tenant-ramp-short` is the five-minute development form; it proves the apparatus can be red and says nothing about
slow leaks. `tenant-ramp-features` is a ten-minute ramp to 4 000 tenants for a feature the host leaves off:
`TRACON_CAPACITY_RATE_LIMIT=instance` turns the per-tenant endpoint limiter on with a limit nothing reaches, and
`TRACON_CAPACITY_TENANT_TAG=1` (through the profile's `tenantTag`) turns the per-tenant metric tag on.
`TRACON_CAPACITY_TENANT_ENDPOINTS=1` makes the model provider report a provider endpoint of its own for each tenant (an
address that ends in `.invalid`; nothing is called), which is what makes the telemetry cost of tenant-chosen endpoints
visible, and `TRACON_CAPACITY_TELEMETRY_LANE_LIMIT=<n>` sets how many of them get telemetry of their own (the product's
value when unset); the host reads this variable and not a `Tracon__…` setting. A rise that is one
jump, level before and after, is written as a note and is never red.

Three more features can be switched on for a ramp, each through a variable the host reports on `/capacity/settings` so
the driver reads it from the running process and the manifest records the same variable:

- `TRACON_CAPACITY_QUOTA=1`: before a tenant's first request the driver writes a quota rule for it (`PUT /api/quotas`, a
  daily limit of a billion runs). Nothing is ever refused; what is measured is what a rule and its counters keep. The rule
  write is an administrative call and leaves an audit entry of its own, so rows of the audit trail grow with the tenants.
- `TRACON_CAPACITY_RESPONSE_CACHE=1`: the fixture agent caches its model responses in the framework's memory cache. Every
  request carries a value of its own, so every lookup misses and every call stores an entry; the cache's own size limit
  and the ten-minute lifetime are the product's defaults.
- `TRACON_CAPACITY_ONLINE_EVAL=1`: every completed run is sampled and queues a judge job; the judge scores at once and
  calls no model, so what is measured is the sampler's and the score window's state for each tenant and the job churn.

`TRACON_CAPACITY_TENANT_TAG=1` is also read for a ramp whose profile does not say `tenantTag`. With the tag on, the run,
cost, judge, cache and job metrics hold a series for every tenant by design: the analysis then judges the series counts of
those instruments as a cost per tenant (steady is accepted, a cost per tenant that climbs is red) and not as a resource
that should not follow the tenants.

Two conveniences help while developing the apparatus (neither is read by anything shipped):

- `TRACON_CAPACITY_MSBUILD_ARGS` adds MSBuild arguments to the pack and the consumer build, for example
  `-m:3 -p:TraconFrontendEnabled=false`. The value is written to the manifest.
- `scripts/capacity.py --ready-feed <directory>` skips the pack and uses a feed packed earlier at the same
  `dirty` version. A publishable version refuses it.

## What the apparatus refuses to do

- Publish a number without its manifest.
- Compare runs whose workload, configuration, dataset or environment differ.
- Turn a heavy profile into a CI gate. Only `smoke` runs in CI, and it checks
  correctness, not speed; no duration here is bound to any threshold.
- Take a connection string as a process argument. It travels only in the
  environment, and every artifact is scanned before it is kept.
