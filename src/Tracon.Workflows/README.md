# Tracon.Workflows

Workflow execution engine for Tracon. It chains catalog agents together
with ready-made patterns, records every run in the `runs` table, and makes
runs resumable from checkpoints.

## Setup

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString)
       .UseWorkflows();
```

## Workflows defined from the UI

A workflow definition is a **graph**, not code: it only carries the names of
catalog agents and a pattern.

| Pattern | What it does |
|-------|----------|
| `Sequential` | Agents run in order; each output feeds the next input |
| `Concurrent` | Agents run at the same time; the results are merged |
| `Handoff` | The first agent starts the work, handing off to others as needed |
| `GroupChat` | A round-robin manager distributes the turn |
| `Magentic` | A manager agent builds a plan, tracks progress, and replans |

```http
PUT /tracon/api/workflows/review
Content-Type: application/json

{
  "kind": "Sequential",
  "agentNames": ["researcher", "writer", "editor"]
}
```

## Workflows defined in code

A free-form graph — custom `Executor` types, conditional edges, sub-workflows
— is defined only in code:

```csharp
builder.AddWorkflow("custom-graph", services =>
{
    var start = ExecutorBindingExtensions.BindAsExecutor<string, string>(
        input => input.ToUpperInvariant(), id: "uppercase");

    return new WorkflowBuilder(start).WithName("custom-graph").Build();
});
```

## Running

```http
POST /tracon/api/workflows/review/run
{ "message": "Review the Q3 report" }
```

The response is SSE. Every frame carries a `RunEvent`; the first frame reports
the run id. Every agent called inside the workflow opens its own `runs` row
and attaches under the workflow row — `GET /api/runs/{id}/tree` returns the
whole tree.

## Checkpoints

A checkpoint is written at every super-step. A run that stopped partway
through can be resumed from where it left off:

```http
GET  /tracon/api/workflows/runs/{runId}/checkpoints
POST /tracon/api/workflows/runs/{runId}/resume
```

Durable resuming requires `UsePostgreSql()`; in an in-memory setup,
checkpoints are limited to the lifetime of the process.

## Limits

| Setting | Default | What it does |
|------|-----------|----------|
| `MaxConcurrentRuns` | 4 | The number of workflows running at the same time |
| `RunTimeout` | 10 min | The maximum duration of a single run. Cooperative: checked between super-steps, so a node that ignores its cancellation token finishes and the run stops at the next boundary |
| `MaxSuperSteps` | 100 | Infinite-loop protection |
| `EnableCheckpointing` | `true` | Checkpoint writing |

This package is **not AOT compatible**: the execution engine uses reflection.
Other Tracon packages are not affected.

## Licence

PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>

## Links

- Guide: <https://tracon.dev/concepts/workflows/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>
