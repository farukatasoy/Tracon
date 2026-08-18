---
title: Reliable runs
description: Configure circuit breaking, idempotency, singleton execution, cancellation, and orphan reconciliation without hiding failure.
---

Reliable agent execution is not one retry switch. Provider calls, HTTP submissions,
background leases, live cancellation, and multi-agent trees fail at different
boundaries. AgentPrism gives each boundary a separate control so recovery stays
explicit.

A useful production baseline is:

```json
{
  "AgentPrism": {
    "CircuitBreaker": {
      "Enabled": true,
      "FailureThreshold": 5,
      "BreakDuration": "00:00:30"
    },
    "Idempotency": {
      "Enabled": true,
      "MaxKeyLength": 255
    },
    "SingletonExecution": {
      "Enabled": true,
      "LeaseDuration": "00:01:00"
    },
    "RunReconciliation": {
      "Enabled": true,
      "HeartbeatInterval": "00:00:30",
      "OrphanThreshold": "00:05:00",
      "ScanInterval": "00:01:00",
      "MaxRunsPerScan": 100
    },
    "AgentGraph": {
      "MaxDepth": 3,
      "MaxTotalTokens": 200000,
      "MaxTotalRuns": 25
    },
    "ModelConcurrency": {
      "MaxConcurrentCallsPerProvider": null
    }
  }
}
```

Enable the SQL provider before treating singleton election, reconciliation, or
idempotency as cluster-wide. Their in-memory stores coordinate only one process.

## Stop calling an unhealthy provider

The circuit breaker is maintained per provider name and per process. After five
consecutive counted failures by default, it opens for 30 seconds. Calls rejected by
an open circuit do not reach the provider and throw
`AgentPrismProviderUnavailableException`; its `ProviderName` and `RetryAfter` tell an
HTTP or job caller when recovery can be tried.

A successful provider call resets the consecutive failure count. Caller cancellation
and `AgentPrismContentBlockedException` do not count as provider failures. Other
provider-client failures do. After the break duration, new calls can probe recovery;
a successful probe closes the circuit.

| Setting | Default | Validation |
|---|---:|---|
| `CircuitBreaker.Enabled` | `true` | When false, every call reaches the provider |
| `FailureThreshold` | `5` | Must be at least `1` |
| `BreakDuration` | 30 seconds | Must be positive |

The breaker fails fast. It does not replay a complete direct run, and it does not
roll back tool calls that already succeeded. Decide retry policy at the HTTP client,
job, or business-operation boundary where idempotency is known.

## Fall back to a secondary provider

`ModelBinding.Fallbacks` is an ordered list of `{ provider, model }` links tried
after the primary binding, without changing today's behavior when the list is
empty:

```json
{
  "provider": "openai",
  "model": "gpt-5.4-mini",
  "fallbacks": [
    { "provider": "anthropic", "model": "claude-opus-5" },
    { "provider": "google", "model": "gemini-3.6-flash" }
  ]
}
```

A fallback link carries only a provider and a model name — none of the primary
binding's temperature, `maxOutputTokens`, or `providerSettings` carry over. Give
the fallback model its own settings through a separate agent if it needs them.

The chain tries the next link only for a transient failure: the local circuit
already open, a `5xx`/`429` response, or a bare connection error. It never falls
back on an authentication error (`401`/`403`) or a canceled request — masking a
misconfigured key behind a silent provider switch costs more than the switch
saves. A provider's own content/safety filter is not a fallback trigger either;
that decision is made once, after whichever link actually answered.

A fallback switch is never silent. The run record gets a `ModelFallbackUsed`
event naming the primary and fallback bindings, and cost and the `runs.model_id`
column both reflect the model that actually answered — not the primary
binding. `RunStatistics.ByModel` groups by the real model too.

If every link in the chain fails, the error surfaces the **first** failure, not
the last one — the root cause across a chain of transient errors is more useful
than whichever link happened to fail last — and its message names every
provider that was tried.

Because the fallback client wraps the whole tool-call loop, switching providers
mid-turn restarts that turn from scratch on the fallback provider. A half-finished
tool conversation cannot be resumed by a different model.

| Setting | Default | Effect |
|---|---:|---|
| `ModelBinding.Fallbacks` | `[]` | Empty preserves today's behavior: an unavailable primary throws |

## Limit outgoing concurrency per provider

`ModelConcurrency.MaxConcurrentCallsPerProvider` bounds how many calls to one
provider can be in flight at once, across every agent that binds to it. The goal
is to avoid producing the burst of `429` responses that would eventually open
the circuit breaker in the first place. A call over the limit **waits** for a
slot — bounded by its own cancellation token — instead of being rejected; an
immediate rejection would turn a short traffic spike into exactly the failure
this setting exists to prevent.

| Setting | Default | Effect |
|---|---:|---|
| `ModelConcurrency.MaxConcurrentCallsPerProvider` | `null` (unlimited) | `null` matches today's behavior; the hot path allocates nothing extra |

## Make supported HTTP submissions idempotent

Add a unique `Idempotency-Key` to a non-streaming request:

```bash
curl -i https://agents.example.com/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $AGENTPRISM_API_KEY" \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: support-ticket-4182-v1' \
  -d '{"message":"Prepare the customer-safe status for ticket 4182."}'
```

The management run endpoint normally streams SSE. Supplying an idempotency key makes
this endpoint return one buffered JSON response so it can be stored and replayed. A
later request with the same tenant, key, method, path, and exact request body receives
the saved response and `Idempotency-Replayed: true`.

Idempotency is attached only to these routes:

- `POST /api/agents/{name}/run`
- `POST /v1/responses`
- `POST /v1/chat/completions`

It does not apply globally to management writes. A request without the header avoids
body buffering and the idempotency-store query.

### Exact outcomes

| Condition | Result |
|---|---|
| First request reserves the key | The endpoint executes |
| Same completed 2xx request | Stored status, body, content type, `Location`, and `Preference-Applied` are replayed |
| Same key is still in progress | `409 Conflict` |
| Same key, different path or raw body | `422 Unprocessable Entity` |
| Key longer than 255 characters by default | `400 Bad Request` |
| Key on a request whose body sets `stream:true` | `400 Bad Request` |
| Idempotency is disabled but the header is present | `501 Not Implemented`; the endpoint does not execute |
| Endpoint returns non-2xx or throws | Reservation is released; a retry can execute |

The fingerprint contains the HTTP method, path, and raw body. Semantically equivalent
JSON with different whitespace or property order is a different request. Generate the
key at the business operation boundary and reuse both the serialized bytes and key on
transport retry.

In a crash after reservation but before completion, an `InProgress` record can remain.
SQL makes that record visible to every node but does not infer whether the side effect
finished. Set a retention policy for idempotency keys and build an operator path for
the rare uncertain result. Configuration retention is off by default; its enabled
fallback for idempotency records is one day.

## Cancel with the right delivery guarantee

Request cancellation with:

```bash
curl -i -X POST \
  -H "Authorization: Bearer $AGENTPRISM_API_KEY" \
  https://agents.example.com/agentprism/api/runs/$RUN_ID/cancel
```

For a registered live run, AgentPrism returns `202 Accepted`. This confirms that the
request reached the owner, not that execution already stopped. Poll the run until it
reaches a terminal status. Providers and tools must observe and pass on the
cancellation token.

The live cancellation registry is in-process by default. A request routed to another
instance, or sent after the owner restarts, returns `409`. Unknown and cross-tenant
runs return `404`; terminal runs return `409`. Queued-run cancellation uses the
shared job store and can work across processes.

Canceling a root run propagates to its child runs. Canceling one child does not cancel
the root or its siblings.

## Reconcile runs left behind by a crash

With run reconciliation enabled, active runs write a bulk heartbeat every 30 seconds
by default. A periodic scan closes `Running` records that have not signaled for five
minutes. The reconciler changes them to `Failed`, records the infrastructure error
type `orphaned`, and appends a `RunFailed` event.

Only `Running` records are reconciled. Queued work belongs to the job queue and its
leases. Heartbeat-write failures are logged, and the next interval tries again.

| Setting | Default | Validation or effect |
|---|---:|---|
| `RunReconciliation.Enabled` | `false` | No heartbeats or scan until enabled |
| `HeartbeatInterval` | 30 seconds | Must be positive |
| `OrphanThreshold` | 5 minutes | Must be positive and at least the heartbeat interval |
| `ScanInterval` | 1 minute | Must be positive |
| `MaxRunsPerScan` | `100` | Must be positive; bounds one pass |

Choose `OrphanThreshold` above the longest expected database pause, runtime stop-the-
world event, or deployment handoff. A threshold that is too short can close a run
whose process is still working.

## Elect one owner for periodic services

`SingletonExecution.Enabled` adds lease-based cluster election to AgentPrism's
periodic model-health refresh, MCP discovery, run reconciliation, approval expiry,
and canary evaluation. It is off by default and uses a 60-second lease. Renewal runs
at one third of the lease duration. An empty `OwnerId` becomes a
machine/process/unique-suffix identifier.

Lease acquisition or renewal errors are logged. The process stops treating itself as
the owner until it acquires a valid lease again. This is coordination, not a guarantee
that an external side effect happens exactly once.

The background job queue does not use this option. It already owns independent job
leases, so turning on singleton execution does not reduce job-worker concurrency.

## Bound multi-agent trees

Every root run creates a budget shared by its child-agent tree:

| Limit | Default | Meaning |
|---|---:|---|
| `AgentGraph.MaxDepth` | `3` | Root is depth zero; negative values clamp to zero |
| `AgentGraph.MaxTotalTokens` | `200,000` | Zero or negative removes the token limit |
| `AgentGraph.MaxTotalRuns` | `25` | Counts child runs only; zero or negative removes the count limit |

A budget prevents a new child run from starting after the boundary is reached. It
does not interrupt work already in progress. Token usage becomes known after a model
call, so concurrent children can produce a bounded overshoot. Use stricter per-run
budgets for externally exposed MCP and A2A agents.

## Failure boundaries at a glance

| Failure | AgentPrism response | Your responsibility |
|---|---|---|
| Consecutive provider errors | Open the local provider circuit and fail fast | Choose whether the whole business operation is safe to retry |
| Primary provider unavailable, transiently | Try the next `ModelBinding.Fallbacks` link and record which one answered | Configure a fallback chain for agents where availability matters more than a fixed model |
| Provider approaching its own rate limit | Queue outgoing calls at `ModelConcurrency.MaxConcurrentCallsPerProvider` instead of bursting | Set a limit sized to the provider's own quota, if bursts are a recurring problem |
| Duplicate supported HTTP request | Replay the completed 2xx response or reject conflict | Keep key and serialized request stable |
| Process dies during a direct run | Reconcile a stale `Running` record when enabled | Make external tool effects idempotent |
| Process dies during a queued job | Release or expire the lease for another worker | Make item processing replay-safe |
| Live cancel reaches wrong node | Return `409` | Use sticky routing or a distributed cancellation registry |
| Child-agent fan-out grows | Refuse new children at tree limits | Set budgets that match cost and latency objectives |

:::caution[Production caveat]
Do not combine automatic retries with side-effecting tools until you can prove an
operation-level idempotency contract. HTTP response idempotency prevents duplicate
execution only on its three supported ingress routes. It cannot undo or deduplicate a
tool call after a process fails. Use SQL for cluster coordination, and monitor lease
age, orphaned runs, open circuits, and `409` cancellation responses.
:::

## Troubleshooting

| Symptom | Check |
|---|---|
| Every call fails fast with provider unavailable | Inspect the provider circuit and `RetryAfter`; fix upstream health before adding retries |
| A run's response came from an unexpected model | Check the run's events for `ModelFallbackUsed`; the primary provider was unavailable for that call |
| A fallback never triggers on a real outage | Confirm the failure is transient (`5xx`/`429`/connection error) — a `401`/`403` or a canceled request never retries by design |
| The same idempotency key returns `422` | Method, path, or raw JSON bytes changed; reuse the original serialization or issue a new key for a new operation |
| A duplicate request returns `409` | The first reservation is still in progress; wait and query the original operation instead of starting another |
| An idempotent request returns `400` | Remove streaming or shorten the key to `MaxKeyLength` |
| An idempotent request returns `501` | Re-enable `AgentPrism:Idempotency` or omit the header and accept normal execution semantics |
| A run stays `Running` after a process crash | Enable reconciliation, use shared SQL state, and confirm singleton election permits a scanner to run |
| Healthy long runs become `orphaned` | Increase `OrphanThreshold`; inspect heartbeat-store latency and failures |
| Two nodes perform the same periodic scan | Confirm singleton execution is enabled and backed by the same SQL database; in-memory leases are process-local |
| A cancel request returns `409` | The run is terminal, the live owner restarted, or the request reached another node |
| A child call is refused | Inspect depth, child-run count, and total-token budget on the root tree |

## Read next

- [Background work](/AgentPrism/guides/background-work/)
- [Observability](/AgentPrism/guides/observability/)
- [Production deployment](/AgentPrism/guides/production/)
- [Runs and replay](/AgentPrism/concepts/runs/)
