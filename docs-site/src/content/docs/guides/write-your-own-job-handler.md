---
title: Write your own job handler
description: Implement a safe IJobHandler for the durable job queue and verify it with AgentPrism's executable contract suite.
---

`IJobHandler` executes a durable job of one `JobKind`. It is registered as a
singleton and dispatched by the background worker; AgentPrism ships handlers
for `AgentBatch`, `Workflow`, and `Eval` the same way.

```csharp
public sealed class NightlyReportJobHandler(ILogger<NightlyReportJobHandler>? logger = null) : IJobHandler
{
    public JobKind Kind => JobKind.AgentBatch;

    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        foreach (var item in context.Items)
        {
            // context.Items is UNFILTERED: a retry hands back items an
            // earlier, now-dead attempt already finished. Skip them.
            if (item.Status != JobItemStatus.Pending)
            {
                continue;
            }

            if (await context.IsCancelledAsync(cancellationToken))
            {
                break;
            }

            logger?.LogInformation("Nightly report generated for '{Subject}'.", item.Input);

            await context.ReportItemAsync(
                new JobItemResult { JobId = context.Job.Id, Seq = item.Seq, Status = JobItemStatus.Completed },
                cancellationToken);
        }
    }
}
```

Register it on the service collection:

```csharp
builder.Services.AddJobHandler<NightlyReportJobHandler>();
```

`AddJobHandler<THandler>()` uses `TryAddEnumerable`, so registering the same
implementation type twice has no effect.

:::caution[Existing kinds are already claimed]
The worker picks a handler with `handlers.FirstOrDefault(h => h.Kind == job.Kind)`
over the DI-resolved `IEnumerable<IJobHandler>`, which .NET resolves in
registration order. `AddAgentPrism()` registers AgentPrism's own handler for
every `JobKind` value before your code runs, so a handler you add after it for
`AgentBatch`, `Workflow`, `Eval`, or any of the other built-in kinds is
registered but never dispatched — the built-in one always wins. This pattern
only works, today, if you register your handler *before* `AddAgentPrism()`
(registration order then puts yours first) and there is no way to add a
genuinely new `JobKind` value from outside the package, since the enum is
closed. Treat `IJobHandler` as verified at the interface level — the contract
below — until this ordering limitation is resolved.
:::

## Runtime contract

**Execution is at-least-once, not exactly-once.** The same job — the same
`JobContext.Job`, with the SAME item list — can reach `ExecuteAsync` more than
once: a thrown exception is retried up to the job's attempt limit
(`JobRetryException.RetryAfter` controls the delay), and a worker that
crashes or whose lease simply expires before it returns lets another worker
(or the same one, on its next poll) re-lease the job and call `ExecuteAsync`
again from scratch. Neither path resets item progress.

`JobContext.Items` reflects that: it carries **every** item, not only the
`Pending` ones. On a retry, items already `Completed` or `Failed` from an
earlier attempt are present too. A handler with a side effect (an email, a
payment, a call to an external system) must therefore either be idempotent on
its own, or — the pattern every built-in handler uses, shown above — check
`JobItemRecord.Status` and skip anything that is not `Pending`.

`IsCancelledAsync` reflects a `POST /api/jobs/{id}/cancel` request; check it
between items so a canceled job stops promptly instead of finishing every
remaining item first. `ReportItemAsync` must be called for every item the
handler actually processes — it updates the item's persisted status and the
job's `doneItems`/`failedItems` counters, which the job list and detail
endpoints read.

A handler that throws is retried by the worker itself; there is no need to
catch and report a job-level failure by hand. Report per-item failures with
`JobItemStatus.Failed` and continue the loop instead, if a single bad input
should not stop the rest of the batch — a batch with both successful and
failed items still completes as `Completed`, and only a batch where every
item failed completes as `Failed`.

## Verify your implementation

Add the contract package to your test project:

```bash
dotnet add package AgentPrism.Testing.Contracts.Xunit --prerelease
```

```csharp
using AgentPrism.Testing.Contracts.Scheduling;

public sealed class NightlyReportJobHandlerTests : JobHandlerContract
{
    protected override ValueTask<IJobHandler> CreateHandlerAsync()
        => ValueTask.FromResult<IJobHandler>(new NightlyReportJobHandler());

    protected override JobItemRecord CreateItem(int sequence, JobItemStatus status)
        => new() { Id = Guid.NewGuid(), JobId = JobId, Seq = sequence, Input = $"customer-{sequence}", Status = status };
}
```

The inherited tests verify that a `Completed` item is not processed again,
that a retry with mixed item statuses only processes the `Pending` ones, and
that cancellation is observed between items. They do not test the queue
itself — leasing, retry scheduling, or persistence; see
[Reliable runs](/guides/reliability/) for that. `samples/AgentPrism.Samples.CustomJobHandler`
in the repository runs this same contract as a package consumer, not a
project reference.

## Read next

- [Jobs, schedules, and queues](/guides/background-work/) — leasing, retries, cancellation, and multi-instance behavior
- [Reliable runs](/guides/reliability/) — what happens to a queued run when a worker dies mid-flight
- [Testing](/guides/testing/) — test package guidance
