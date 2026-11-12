---
title: Webhooks
description: Subscribe an endpoint to signed Tracon events. Which events exist, what a payload carries, how the signature and secrets work, and what a delivery guarantees.
sidebar:
  order: 9
---

Subscribe to events and Tracon posts them to your endpoint. This page lists the events,
says what a payload carries, shows how to verify the signature and where the secrets
live, and states what a delivery guarantees. Subscriptions are managed through the
[webhooks HTTP API](/http-api/webhooks/) and the console.

## Events

| Event | Fires when |
|---|---|
| `run.completed` | A root run completed successfully — an agent run or a workflow run (`run.kind`) |
| `run.failed` | A root run ended in an error — an agent run or a workflow run (`run.kind`) |
| `approval.pending` | A tool call is awaiting approval |
| `workflow.request.pending` | A workflow run is waiting for human input — one event per request |
| `job.completed` | A queued job completed successfully |
| `job.failed` | A queued job ended in an error on its last attempt |
| `eval.completed` | An evaluation run reached its final status: completed, cancelled, or failed on its last attempt |
| `quota.threshold` | A quota counter crossed a percentage in `Quotas:ThresholdPercents` — once per period |
| `run.score.low` | The online-evaluation window's average score dropped below `OnlineEvaluation:LowScoreThreshold`, once the minimum sample count is met |
| `test.ping` | You sent a test delivery to verify the subscription |

## The payload

A payload is a summary, never content. A root run's `run.*` event carries the call
tree's tokens and cost as the tree stands when the root closes: an agent root adds its
child agents to its own, and a workflow root (which has none of its own) sums its steps.
A child that ends after its root is not in it; the quota counter and `GET /api/runs/{runId}`
carry the exact total. Child runs publish nothing. `cost` is the token cost and the amount the tree
[reported beyond tokens](/concepts/workflow-function-nodes/#report-what-a-node-spent-beyond-tokens)
together; `nonTokenCost` is the reported part alone and is left out when nothing was
reported, so the token part is `cost - nonTokenCost`. `workflow.request.pending` carries the request id, the
run id and the workflow name — read the prompt and the form from
`GET /api/workflows/runs/{runId}/requests`. `job.*` carries the job id, its handler key
(`kind`), its target, its item counts and a display-safe error; a cancelled job publishes
nothing, and a webhook delivery job never publishes `job.*` (its own event would queue
another delivery). `eval.completed` carries the evaluation run id, the suite, the agent
and its version, the model, the case counts and the tokens.

## Signature and secrets

The signature is `HMAC-SHA256(timestamp + "." + body, secret)`, with the timestamp
inside the signature so a replay cannot be reused. Your receiver decides the tolerance
window.

The secret is never stored: the subscription carries the **name** of the configuration
key it is read from. A receiver that wants its own credential header — `X-Api-Key`,
`Authorization` — gets it the same way: `headerConfigurationKeys` maps the header name
to a key under `Tracon:WebhookSecrets:`, and the value is read on every delivery. A
subscription whose key name sits outside its tenant has the delivery dropped. The
names of both header maps enter the audit trail; no header value does.

## Delivery

Delivery goes through the job queue, so a `test` call reports that it was queued, not
how it went. The delivery history has one entry per event carrying the latest status and
attempt count — retries update that entry rather than adding rows.

A delivery your receiver accepted with a `2xx` is not sent again because Tracon could
not record the result. Tracon tries the result writes again for a third of the job
lease; a write that still fails is logged with a reference, and the history entry
keeps its earlier status. A retry of the job that finds the entry already `Delivered`,
`Failed`, or `Dropped` sends nothing. Delivery is still at-least-once: a worker that
stops between the send and the record can send the same event twice, so deduplicate
on the `X-Tracon-Delivery` header, which carries the delivery id.

Address validation happens inside the socket connect callback, so the address
validated is the address connected to. See
[securing the endpoints](/getting-started/security/) for why.

## Read next

- [Governance](/concepts/governance/) — the audit trail that records every subscription change, and the quotas that raise `quota.threshold`.
- [Inbound triggers](/guides/inbound-triggers/) — the reverse direction: another system signs a request and starts a run.
- [Securing the endpoints](/getting-started/security/) — bind endpoint authorization and tenant resolution to your host.
