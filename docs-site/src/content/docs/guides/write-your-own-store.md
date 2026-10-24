---
title: Write your own store
description: Implement IRunStore against your own persistence engine and prove it correct with the same contract suite the four shipped providers run.
---

:::note[Preview packages]
Tracon is published as `1.0.0-preview.5`. Use `--prerelease` for discovery or
pin the exact version for reproducible builds.
:::

The three built-in providers (`Tracon.PostgreSql`, `.SqlServer`, `.Sqlite`) cover
most deployments. When none of them fit — a document database, an existing
event-sourced system, a managed table service — `IRunStore` and the 32 other store
interfaces in `Tracon.Abstractions` are the extension point. This guide covers the
largest and most-documented one, `IRunStore`, using its behavior contract to verify
the result.

```bash
dotnet add package Tracon.Testing.Contracts.Xunit --prerelease
```

Add it to your **test** project only. It brings in `Tracon.Abstractions` and
nothing else from Tracon — no `Tracon.Core`, no SQL package, no ASP.NET Core.
xunit.v3 and Shouldly are ordinary dependencies: your test project's own xunit.v3
runner discovers the `[Fact]` methods the inherited contract class declares. The
package asks for xunit.v3 `3.2.2` or newer; it was run on xunit.v3 `4.0.2` with
`Microsoft.Testing.Extensions.TrxReport` `2.5.1`. On the .NET 10 SDK, xunit.v3 4
also needs `"test": { "runner": "Microsoft.Testing.Platform" }` in your `global.json`;
without it `dotnet test` stops with "Testing with VSTest target is no longer supported".

## Implement the interface

```csharp
public sealed class MyRunStore : IRunStore
{
    public ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken ct = default) { /* ... */ }
    public ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken ct = default) { /* ... */ }
    public ValueTask<long?> GetLastEventSequenceAsync(Guid runId, CancellationToken ct = default) { /* ... */ }
    public ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken ct = default) { /* ... */ }
    public ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken ct = default) { /* ... */ }
    public ValueTask<RunStatistics> GetStatisticsAsync(RunStatisticsQuery query, CancellationToken ct = default) { /* ... */ }
    public IAsyncEnumerable<RunEvent> ReadEventsAsync(Guid runId, long fromSequence = 0, CancellationToken ct = default) { /* ... */ }
    public ValueTask RecordToolInvocationAsync(ToolInvocationRecord invocation, CancellationToken ct = default) { /* ... */ }
    public ValueTask<bool> CompleteLateToolInvocationAsync(LateToolCompletion completion, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(Guid runId, CancellationToken ct = default) { /* ... */ }
    public ValueTask RecordNonTokenUsageAsync(NonTokenUsageRecord record, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<NonTokenUsageRecord>> ListNonTokenUsageAsync(Guid runId, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(ToolUsageQuery query, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(ExperimentResultsQuery query, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery query, CancellationToken ct = default) { /* ... */ }
    public ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, string? tenantId, CancellationToken ct = default) { /* ... */ }
    public ValueTask TouchHeartbeatAsync(IReadOnlyCollection<Guid> runIds, DateTimeOffset at, CancellationToken ct = default) { /* ... */ }
    public ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(DateTimeOffset staleBefore, int max, CancellationToken ct = default) { /* ... */ }
}
```

Nineteen methods, and eight behavior axes the contract suite checks that a compiler
cannot:

| Axis | Rule |
|---|---|
| Idempotency | `StartRunAsync` is an UPSERT. A second call with the same `RunId` updates the row instead of opening a new one; `UserId`, `Labels` and `ContinuedFromRunId` are COALESCED (a `null` value on the call leaves the previous value in place), and the **returned** record reflects that COALESCE, not the raw call arguments. Workflow recovery depends on the link: it writes a continuation's row with `ContinuedFromRunId`, and the engine writes the same row again without it |
| Tenant behavior | Three modes across the interface: **expected tenant** (`AppendEventAsync`, `CompleteRunAsync`, `UpdateRunCostAsync` compare the call's tenant against the record's own, ignoring the ambient tenant), **ambient tenant** (the read methods filter by `ITenantContext` unless overridden), **tenant-independent** (`TouchHeartbeatAsync`, `ClaimOrphanedRunsAsync` are maintenance work that scans every tenant) |
| Thread safety | Registered as a singleton; must be safe under concurrent calls from unrelated runs and must not depend on a scoped service |
| Null / not-found semantics | `GetRunAsync` returns `null`; `ReadEventsAsync` returns an empty sequence; `AppendEventAsync` and `CompleteRunAsync` throw `TraconException` |
| Completion overrides | `CompleteRunAsync` may correct the model attribution a run started with: `RunCompletion.ModelId` and `RunCompletion.ModelProvider` each **overwrite** the stored value when set, and **leave it unchanged** when `null` — the common case, since only a `ModelBinding.Fallbacks` link answering makes a completion carry them. Persisting `ModelId` but forgetting `ModelProvider` costs the run its provider attribution silently, and the cost resolver then falls back to matching by model name alone |
| Non-token cost | What a run reports beyond tokens is a total of its own, never a term of the token cost. `CompleteRunAsync` stores `RunCompletion.NonTokenCost` on the run, and `RunRecord.NonTokenCost` and `TreeNonTokenCost` read it back: `null` means the run reported nothing and is never `0`, an item that carried no amount keeps its count in `UnsummedItemCount` with an `Amount` of `null`, and the tree total counts the run itself and every run below it once. `RecordNonTokenUsageAsync` writes an item at once, and when `NonTokenUsageRecord.TenantId` is set it writes only for a run of that tenant. `ListNonTokenUsageAsync` returns the run's own items, not its children's, in the order they were reported, and an empty list for a run that does not exist or belongs to another tenant |
| Event order | `RunEvent.Sequence` is assigned by the caller, not the store; events are append-only; `ReadEventsAsync`'s `fromSequence` is inclusive |
| Duplicate `Sequence` | A second `AppendEventAsync` call reusing a sequence number already written for that run is a caller error — reject it with `TraconException`, do not silently accept or silently drop it |

The full text lives on `IRunStore`'s own XML documentation — see [the API
reference](/api/tracon.irunstore/) for the exact wording each method carries.

## Prove it with the contract suite

```csharp
using Tracon.Testing.Contracts.Storage;

public sealed class MyRunStoreTests : RunStoreContract
{
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => new(new MyRunStore(/* your dependencies */));
}
```

`dotnet test` now runs every scenario `RunStoreContract` defines: the idempotency and
duplicate-`Sequence` rules above, two-directional tenant isolation (a tenant reads its
own runs and never another tenant's), statistics aggregation computed *inside* the
store rather than by fetching rows into memory, tree totals across parent/child runs,
and error-cluster grouping. A method your store has not implemented yet makes the
inherited test **fail** with whatever exception that method throws — it does not fail
to compile, so an incomplete store is still a normal, greppable red build.

Two parts of `IRunStore` have a contract class of their own, derived the same way: `ToolInvocationContract`
for the tool-call records (it also checks that a measurement's provider and model survive
the round trip) and `NonTokenUsageContract` for `RecordNonTokenUsageAsync` and
`ListNonTokenUsageAsync`. `RunStoreContract` covers the snapshot that comes back on the run.

:::caution[Testing against a prerelease build]
If you point your project at a **prerelease** Tracon package that you built
yourself rather than one from nuget.org, give the run its own package cache:

```bash
NUGET_PACKAGES=$(mktemp -d) dotnet test
```

A prerelease version string can name different content over time. NuGet caches
by package id and version, so once `1.0.0-preview.5` is extracted into your
global cache, a later build carrying the same version is never re-read — your
store then compiles against the older contract and the contract suite reports
failures that do not exist in the package you just built.
:::

```mermaid
flowchart TD
    accTitle: How the contract suite verifies a custom store
    accDescr: A test class derives RunStoreContract and supplies a store instance. The base class runs idempotency, tenant isolation, event ordering, and statistics scenarios against it. A scenario that fails names the exact behavior axis that is wrong.
    IMPL["Your IRunStore implementation"] --> DERIVE["Test class : RunStoreContract<br/>CreateStoreAsync() returns it"]
    DERIVE --> RUN["dotnet test"]
    RUN --> IDEMP["Idempotency + duplicate Sequence"]
    RUN --> TENANT["Tenant isolation, both directions"]
    RUN --> STATS["Statistics computed in the store"]
    RUN --> TREE["Tree totals across parent/child runs"]
```

## Cancellation: the one promise every store makes

Every store contract in the package — not just `RunStoreContract` — asks the same
two questions about a `CancellationToken` that was **already cancelled** when the
call started:

| Scenario | What your store must do |
|---|---|
| `Canceled_token_throws_on_read` | Throw `OperationCanceledException` from a read |
| `Canceled_token_throws_on_write_and_leaves_no_trace` | Throw from a write **and** leave no record behind |

The second one is the one to design for. Throwing is not enough on its own: a check
placed after the work throws too, and the row is already written. The contract reads
the store back and requires it to be untouched, so the check belongs **before** the
work:

```csharp
public ValueTask<MyRecord> SaveAsync(MyRecord record, CancellationToken cancellationToken = default)
{
    ArgumentNullException.ThrowIfNull(record);
    cancellationToken.ThrowIfCancellationRequested();   // before the write, not after

    // ... write
}
```

A store built on ADO.NET usually satisfies this for free: `ExecuteReaderAsync(token)`
and `OpenAsync(token)` observe an already-cancelled token themselves. A store built on
in-process state, a file, or an HTTP client that does not take the token has to say so
in one line per method.

Two limits are deliberate. Cancellation **during** a call is not covered — it races,
so no deterministic scenario can assert where the work stopped. And the contract
exercises one read and one write per store rather than every method; that is enough
to prove the rule is implemented, not a substitute for applying it everywhere.

Your store's **own** timeout is not a cancellation, even when it arrives as one. An
HTTP client reports `HttpClient.Timeout` as `TaskCanceledException`; Tracon tells the
two apart by the token, not by the type. While the token it passed is not cancelled,
an `OperationCanceledException` from your store is a store failure and gets the same
treatment as any other: a recording write is dropped and counted, the run continues, a
job attempt is retried, and an HTTP endpoint answers its documented error. You do not
need to convert it yourself.

A method returning `IAsyncEnumerable<T>` is lazy: its body does not run until the
first step. `RunStoreContract` therefore asks that the **first** `MoveNextAsync`
throws, including on a run with no events at all — the case a store that only checks
the token between yields misses.

## What the suite does not check

The same three things every store contract leaves to you, regardless of interface:

- **Performance at scale.** The suite proves correctness on a handful of records; it
  is not a load test.
- **Your own persistence engine's guarantees.** If your backing store does not offer
  atomic upserts, `StartRunAsync`'s idempotency requirement is your problem to solve,
  not something the contract can verify for you.
- **Wiring into `AddTracon()`.** The contract suite exercises the store directly.
  Registering it (`services.AddSingleton<IRunStore, MyRunStore>()`, called before or
  after `AddTracon()` — `TryAdd*` means your registration always wins) is a
  separate, ordinary DI step.

## Other store interfaces

`IRunStore` is the largest and most heavily documented seam, but the same package
ships a contract class for 30 of the other store interfaces in `Tracon.Abstractions` —
sessions, agent definitions, jobs, evals, experiments, webhooks, and more. Each follows
the same shape: derive the matching `*Contract` class, supply a store instance, run
`dotnet test`.

Four have no contract yet: `IConversationBranchStore`, `IDataSubjectStore`,
`ITenantStore` and `IVectorSearchStore`. Implementing one of those is supported;
you write your own tests for it.

`IJobStore`'s contract (`JobStoreContract`) also verifies lane behavior: `LeaseAsync`
takes a `JobLeaseRequest` whose `Lanes` it must honor (only a job from one of them, `null`
or an empty list applies no filter), and a retried job keeps its lane. See
[Background work](/guides/background-work/) for what a lane is and how a job's lane is
chosen.

Two more `IRunStore` and `IJobStore` rules carry
[workflow recovery](/concepts/workflows/#resume-an-interrupted-run-automatically), and
both contracts check them:

- **`ReportItemAsync` changes only a pending item.** A report for an item that
  already completed or failed changes nothing. A `Pending` report is a marker: it
  writes the item's run id before the run starts and counts nothing, so a job leased
  again after a crash finds the run its item was processing.
- **`RunQuery.ContinuedFromRunId` filters by the continued run**, within the query's
  tenant. Recovery walks a continuation chain forward with it.

Two more rules carry the
[per-tenant workflow limit](/guides/background-work/#limit-workflow-runs-per-tenant):

- **`IJobStore.LeaseAsync` honors `JobLeaseRequest.TenantCap`.** A ready job whose
  handler key is in the cap is skipped while its tenant already holds
  `MaxLeasedPerTenant` live leases on those keys (an expired lease does not count);
  the oldest job that is still eligible is leased instead. With no cap the oldest
  ready job is leased whichever tenant owns it. The count may be read without a lock.
- **`IRunStore` marks and closes a `Queued` row that has a heartbeat.**
  `TouchHeartbeatAsync` writes `Running` and `Queued` rows, and `ClaimOrphanedRunsAsync`
  closes a `Queued` row whose heartbeat is stale as `Failed` (`orphaned`). A `Queued`
  row that never sent a heartbeat belongs to the job queue and is never closed by it.
  A second `StartRunAsync` leaves the heartbeat as it was.

`ISessionStore`'s contract (`SessionStoreContract`) checks the two rules that make
per-user [session ownership](/concepts/sessions/#session-ownership) work, and they are
easy to miss in a custom store because everything else keeps passing without them:

- **`SessionQuery.OwnerId` filters before `Skip`/`Take`.** The contract seeds five
  owned and five unowned sessions with the unowned ones newest, then asks for
  `Take = 3`. A store that pages first and filters the page afterwards returns fewer
  than three rows — and the gaps let a caller count how many sessions other users
  hold. A row with a `null` owner matches no owner filter at all: an unowned session
  is nobody's, not everybody's.
- **A write that carries no owner does not clear one already stored.** A session is
  written more than once, and a later write can come from a background worker with no
  request behind it. The three shipped SQL stores `COALESCE` the column for exactly
  this reason. Without the rule, a session silently drops out of its owner's list on
  the second write while every first-write test stays green.

A store that ignores `OwnerId` still compiles and still runs — session ownership just
never works on it. Running `SessionStoreContract` is what makes that visible.

`IWorkflowDefinitionStore` keeps the [version history](/concepts/workflows/#versions-rollback-and-pinning-an-agent-step)
of a stored workflow, so it has three members beside the record-level ones:
`GetVersionAsync`, `ListVersionsAsync` and `RollbackAsync`. A store written against an
earlier preview does not compile until it adds them. `WorkflowDefinitionStoreContract`
runs the history scenarios, and three rules are easy to miss:

- **A version number is an identity.** A run records the version it was compiled from, so
  one number must never name two contents. The history outlives the workflow's row:
  deleting a workflow keeps its versions, and the next save continues from the highest
  number ever written.
- **A save writes the current row and its history entry together.** A failed history
  write must leave the current definition as it was. The contract proves this only for a
  store that overrides its raw-history hooks (`SupportsRawHistory`).
- **A rollback appends.** It writes the old content as a new version and never rewrites an
  entry; concurrent saves still get distinct numbers.

`IJobStore` carries one aggregate method alongside the record-level ones.
`GetQueueDepthAsync` counts outstanding jobs grouped by lane and status, the way
`IRunStore.GetStatisticsAsync` sits on the run store rather than in an interface of
its own. Three rules the contract checks, and that a custom store has to honor:

- Count **only** the open statuses — `Pending`, `Leased`, and `Running`. A terminal
  job is counted by `tracon.job.executions` when it finishes, so scanning for it
  here would tie the query's cost to the queue's whole history.
- Omit a lane/status pair with no open jobs rather than reporting it as zero.
- Take **no tenant argument and apply no tenant filter**. Like `LeaseAsync`, this is
  a question about the worker pool, which leases across every tenant. The shipped SQL
  stores answer it from the same partial index that backs leasing.

## Read next

- [Persistence](/getting-started/persistence/) — the three shipped providers, for
  comparison
- [Runs and recording](/concepts/runs/) — what `IRunStore` records and why
- [Reference: every public type](/api/) — generated from the shipped XML documentation
