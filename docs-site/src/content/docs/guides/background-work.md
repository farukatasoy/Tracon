---
title: Background work
description: Queue agent runs, schedule recurring work, operate workers, and understand leasing, retries, cancellation, and multi-instance behavior.
---

AgentPrism can move a run out of the request path or create work from a recurring
schedule. Both paths use the same durable job queue and worker. This gives an API
caller a short `202 Accepted` response while AgentPrism owns execution, status, and
recovery.

Use background work when the caller cannot keep an SSE connection open, when a batch
contains many independent inputs, or when work must start on a calendar. Keep the
normal streaming run endpoint for interactive conversations and approval flows.

## Configure the worker

`AddAgentPrism()` registers the scheduling stores and worker. `UseScheduling()` only
changes its settings:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.UseScheduling(options =>
{
    options.RunWorker = true;
    options.MaxConcurrentJobs = 4;
    options.PollInterval = TimeSpan.FromSeconds(5);
    options.LeaseDuration = TimeSpan.FromMinutes(5);
    options.MaxAttempts = 3;
    options.MaxItemsPerJob = 1_000;
});

var agentPrism = builder.AddAgentPrism()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(
        builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName));

var app = builder.Build();
app.MapAgentPrism("/agentprism");
app.Run();
```

The same values can come from configuration:

```json
{
  "AgentPrism": {
    "Scheduling": {
      "Enabled": true,
      "RunWorker": true,
      "MaxConcurrentJobs": 4,
      "PollInterval": "00:00:05",
      "LeaseDuration": "00:05:00",
      "MaxAttempts": 3,
      "MaxItemsPerJob": 1000
    },
    "AsyncRun": {
      "Enabled": true,
      "MaxAttempts": 1
    }
  }
}
```

Code passed to `UseScheduling()` wins over configuration.

## Queue one agent run

Send the normal management run request with `Prefer: respond-async`:

```bash
curl -i https://agents.example.com/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $AGENTPRISM_API_KEY" \
  -H 'Content-Type: application/json' \
  -H 'Prefer: respond-async' \
  -d '{"message":"Prepare the weekly escalation report."}'
```

AgentPrism returns `202 Accepted`, `Preference-Applied: respond-async`, and a
`Location` header. The response identifies both the run and its backing job. For this
path, the job ID is the run ID. Follow the returned location, or read the run and its
events directly:

```bash
curl -sS \
  -H "Authorization: Bearer $AGENTPRISM_API_KEY" \
  https://agents.example.com/agentprism/api/runs/$RUN_ID

curl -sS \
  -H "Authorization: Bearer $AGENTPRISM_API_KEY" \
  https://agents.example.com/agentprism/api/runs/$RUN_ID/events
```

Queued runs do not accept `attachmentIds` or an initial `approvals` collection. If a
tool asks for approval later, the queued request must include a `sessionId`. Decide
the pending approval through the approval endpoint, then start a new run in that
session to resume the conversation.

`AsyncRun.MaxAttempts` defaults to one. This is deliberate: an expired lease must not
silently repeat an agent run that already changed an external system. Raise it only
when every tool in the run is idempotent and you have tested replay after a worker
crash.

## Create a recurring schedule

The schedule name is part of the URL. The body selects the fixed job kind, target,
calendar, time zone, and payload:

```bash
curl -sS -X PUT \
  https://agents.example.com/agentprism/api/schedules/weekday-support-review \
  -H "Authorization: Bearer $AGENTPRISM_API_KEY" \
  -H 'Content-Type: application/json' \
  -d '{
    "kind": "AgentBatch",
    "targetName": "support",
    "cron": "0 8 * * 1-5",
    "timeZone": "UTC",
    "payload": [
      "Review unresolved priority-one tickets.",
      "Summarize breaches of the response-time objective."
    ],
    "enabled": true
  }'
```

A JSON array creates one item for each element. Any other JSON value creates one
item. A missing payload creates no items. Each job accepts at most
`MaxItemsPerJob` items.

AgentPrism accepts five-field cron expressions: minute, hour, day of month, month,
and day of week. It supports `*`, fixed values, ranges, lists, and `/step`. It does
not support seconds or the `L`, `W`, and `#` extensions. When both day-of-month and
day-of-week are restricted, either field can match. Time-zone identifiers are
validated by `TimeZoneInfo` on the host.

Useful operations are:

| Operation | Endpoint |
|---|---|
| List schedules | `GET /api/schedules` |
| Read, replace, or delete one schedule | `GET`, `PUT`, or `DELETE /api/schedules/{name}` |
| Trigger now | `POST /api/schedules/{name}/trigger` |
| List jobs | `GET /api/jobs?kind=&status=&scheduleId=&skip=&take=` |
| Inspect one job and its items | `GET /api/jobs/{id}` |
| Request cancellation | `POST /api/jobs/{id}/cancel` |

Manual trigger works even when the schedule is disabled. It does not change the next
calendar occurrence. Deleting a schedule does not cancel jobs that it already
created.

## Separate API and worker processes

All application instances run a worker by default. Set `RunWorker=false` on API-only
instances. Start one or more worker instances with the same storage, provider, agent,
and handler registrations:

```csharp
builder.Services.UseScheduling(options =>
{
    options.RunWorker = builder.Configuration.GetValue<bool>("Worker:Enabled");
});
```

`MaxConcurrentJobs` is a per-process limit. Four worker processes with the default of
two can execute up to eight jobs at once. SQL leasing prevents two processes from
owning the same job at the same time, and schedule claiming prevents duplicate
calendar dispatch. PostgreSQL uses a non-blocking row-lock strategy for competing
workers.

## Singleton services and orphaned-run recovery

Two more background concerns sit next to the job queue, both off by default and
both meant for a multi-instance deployment.

**Singleton execution** (`AgentPrism:SingletonExecution`) elects one active
instance for periodic background services that must not run twice at once:
approval expiration, model-provider health polling, canary evaluation, and run
reconciliation itself. It does not protect the job queue — that already uses
per-job SQL leasing, which is unaffected by this setting.

```json
{
  "AgentPrism": {
    "SingletonExecution": {
      "Enabled": true,
      "LeaseDuration": "00:01:00"
    }
  }
}
```

Renewal happens at one third of `LeaseDuration`, so a single missed renewal
still leaves two more attempts before another instance can take over.
`OwnerId` defaults to the machine name, process id, and a random suffix; set
it explicitly only to force a specific instance to hold the lease.

**Run reconciliation** (`AgentPrism:RunReconciliation`) closes runs stuck in
`Running` after the owning process crashes mid-execution — a `Queued` job is
already covered by lease expiry, but a run that already started has no lease
of its own. Without reconciliation, an orphaned row stays `Running` forever
and dilutes the run error rate. An active run writes a bulk heartbeat every
`HeartbeatInterval`; a singleton-elected scanner then looks for rows silent
longer than `OrphanThreshold` and marks them `Failed`.

```json
{
  "AgentPrism": {
    "RunReconciliation": {
      "Enabled": true,
      "HeartbeatInterval": "00:00:30",
      "OrphanThreshold": "00:05:00",
      "ScanInterval": "00:01:00",
      "MaxRunsPerScan": 100
    }
  }
}
```

Both settings work with the in-memory lease store, but that store only
coordinates within one process. Run PostgreSQL, SQL Server, or SQLite to get
real cross-instance election.

## Defaults and limits

| Setting | Default | Limit or effect |
|---|---:|---|
| `Scheduling.Enabled` | `true` | `false` stops dispatch and worker execution; APIs and stores remain available |
| `Scheduling.RunWorker` | `true` | `false` disables the worker in this process |
| `Scheduling.MaxConcurrentJobs` | `2` | Must be at least `1`; applies per process |
| `Scheduling.PollInterval` | 10 seconds | Must be positive |
| `Scheduling.LeaseDuration` | 5 minutes | Must be positive; renewal starts halfway through the lease |
| `Scheduling.MaxAttempts` | `3` | Must be at least `1`; jobs can override it |
| `Scheduling.MaxItemsPerJob` | `1,000` | Must be at least `1` |
| `AsyncRun.Enabled` | `true` | A queued run request returns `501` when disabled |
| `AsyncRun.MaxAttempts` | `1` | Controls queued agent-run retries |
| Job list page | 50 records | `skip` defaults to `0`; `take` defaults to `50` |
| `SingletonExecution.Enabled` | `false` | `true` elects one instance for the singleton services above |
| `SingletonExecution.LeaseDuration` | 60 seconds | Renewal at one third of this duration |
| `RunReconciliation.Enabled` | `false` | `true` closes orphaned `Running` rows after a crash |
| `RunReconciliation.HeartbeatInterval` | 30 seconds | How often an active run's process signals it is alive |
| `RunReconciliation.OrphanThreshold` | 5 minutes | Silence beyond this marks a run orphaned |
| `RunReconciliation.ScanInterval` | 1 minute | Wait between reconciliation passes |
| `RunReconciliation.MaxRunsPerScan` | `100` | Upper bound on rows closed per pass |

The cron search looks ahead for at most four years. A nonexistent local time during a
spring daylight-saving transition is skipped. A duplicated local minute during the
fall transition maps to the earlier occurrence.

## Delivery and failure behavior

The queue provides exclusive leases, not exactly-once side effects. A process can
fail after an external call succeeds but before its item is committed. A lease can
then expire and another worker can see that item again. Tool calls and custom job
side effects must therefore use their own idempotency key or transactional boundary.

- A handler exception is retried until the job's attempt limit is reached.
- `JobRetryException.RetryAfter` asks the queue to delay the next attempt. Other
  failures can be retried immediately.
- A missing handler marks the job as failed.
- Lease-renewal, polling, and dispatch errors are logged; later polling ticks continue.
- Cancellation is cooperative. A running handler sees cancellation between items and
  must also pass its token to long operations.
- A batch with both successful and failed items finishes as `Completed`. A batch in
  which every item fails finishes as `Failed`. Inspect item counters, not only the job
  status.

:::caution[Production caveat]
In-memory job and schedule stores disappear when the process stops and cannot
coordinate multiple instances. Use PostgreSQL or SQL Server for durable workers.
SQLite is suitable for one process only. Keep the lease longer than the longest
normal operation between cancellation checks, and make all external side effects
idempotent.
:::

## Troubleshooting

| Symptom | Check |
|---|---|
| A queued run remains `Queued` | Confirm `Scheduling.Enabled` and `RunWorker`; confirm at least one worker passed the schema-ready gate and can reach the same SQL database |
| The request returns `501` | `AgentPrism:AsyncRun:Enabled` is false |
| The request returns `400` | Supply `message`; remove `attachmentIds` and initial `approvals` from a queued run |
| The same external action happens twice | The lease was replayed after an uncertain failure; add an operation-level idempotency key and leave queued-run attempts at one until the tool is safe |
| A job is `Completed` but work is missing | Inspect `failedItems` and the individual item records; mixed-result batches still complete |
| A schedule never fires | Validate the five-field cron, host time-zone identifier, enabled flag, and next occurrence; remember that daylight-saving gaps are skipped |
| More jobs run than expected | Multiply `MaxConcurrentJobs` by the number of worker processes |
| Cancellation takes time | It is a request, not a forced thread abort; pass the cancellation token through every long-running handler operation |
| A run stays `Running` forever after a crash | Enable `RunReconciliation`; confirm a SQL store is registered and `OrphanThreshold` is not longer than an acceptable outage window |
| Two instances both act as the singleton service | Confirm `SingletonExecution.Enabled` and that all instances share the same SQL store; the in-memory lease store cannot coordinate across processes |

## Read next

- [Reliable runs](/AgentPrism/guides/reliability/)
- [Production deployment](/AgentPrism/guides/production/)
- [HTTP API conventions](/AgentPrism/http-api/)
