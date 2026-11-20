---
title: Run errors
description: How a failed run carries an error class, a fingerprint and a safe message, and what truncated and filtered output mean.
sidebar:
  order: 3
---

A failed run keeps what went wrong as data you can group and count, not only as a sentence. This page is
the part of [Runs and recording](/concepts/runs/) that explains the error class, the fingerprint, the text
that is safe to show and the two cases where an answer ends early without a failure.

```mermaid
flowchart LR
    accTitle: How a failure becomes a classified run error
    accDescr: A failure on a run is classified into an error class and a clustering fingerprint, its message is made safe to store, and the full detail goes to the server log under the same correlation id.
    FAIL["A run fails"] --> CLS["Classifier<br/>built in, or your own"]
    CLS --> ROW["Run record<br/>error type, class, fingerprint"]
    FAIL --> MSG["Message made safe<br/>type name and ref for a foreign exception"]
    MSG --> ROW
    FAIL --> LOG["Server log<br/>full detail, same ref"]
    ROW --> STATS["GET /api/stats<br/>counts by class"]
```

## Errors are classified

A failed run carries an error class, not just a message: a provider outage, a content
filter, a blocked guard decision, a quota, a timeout, a rejected structured response, a
structured response cut off at the output limit (`OutputTruncated`, see
[Truncated output](#truncated-output)), a
harness loop that ran out of iterations unsatisfied (`LoopUnsatisfied`, see
[failing the run](/concepts/agents/#fail-the-run-when-the-loop-is-not-satisfied)), a
provider the tenant's rules refused before any call (`TenantProviderRefused`, error type
`tenant_provider_refused`: its egress policy does not allow the provider, or it has its
own credential for a provider that cannot use one). A provider that answered `429` is
`RateLimited`, also when the provider SDK's failure was masked as `upstream_error`.
Two of them are deliberately distinct — `ContentFiltered` means the *provider's*
filter cut the response, while `ContentBlocked` means *your* guard refused it. The
operator response differs: one is a provider setting, the other is your policy.

`GET /api/stats` aggregates the classes, so a rise in one bucket is visible before
anyone reports it.

A workflow run is classified the same way. Its `error.type` is the same stable
identity the failing step's own run records — `loop_unsatisfied`,
`child_run_timeout`, `run_budget_exceeded`, `tenant_quota_exceeded` — not the exception's .NET type name,
and its class is the step's class.

Both the class and the clustering fingerprint come from a classifier you can
replace or compose with your own rules — see
[Write your own error classifier](/guides/write-your-own-error-classifier/).

### Truncated output

A model that reaches its output token limit stops in the middle of its answer and
reports a `length` finish reason. Tracon reads that reason for every provider and
treats the two kinds of answer differently:

| The agent asks for | Outcome |
|---|---|
| A structured response (`Json` or `JsonSchema`) | The run fails with error type `output_truncated`, class `OutputTruncated`. A cut-off JSON document cannot be parsed. No repair turn runs, because it would hit the same limit; the `StructuredResponseRejected` event carries `repairSkippedTruncated: true` when a repair attempt was left |
| Plain text | The run completes with the partial answer and writes a `Custom` run event of type `tracon.output.truncated`. Its payload names the provider, the limit when the binding set one, and the `cause` — never the answer text |

Only the turn that ends the run decides. A tool call the model was still writing when
it hit the limit is an intermediate turn: the tool loop runs it and asks the model
again. The tokens of a failed truncated call still count in the run's usage, cost and
quota.

A client streaming the run learns about a cut-off plain-text answer before the stream
ends: the `tracon.output.truncated` event arrives as a `custom` frame right before
`done`, with the same data the run's events carry. Each recorded event is one frame, so
a harness loop whose iterations were cut sends more than one, and a tool turn cut in
the middle of the run sends none. A cut-off structured answer ends the stream with an
`error` frame instead. No event, and no frame, is written while run recording is off
(`Tracon:RunRecording:Enabled`).

The `update` frames also carry a `finishReason` field. It is the model's raw reason
for each model call — `tool_calls` on a tool turn, possibly reported more than once in
a loop — passed through from Microsoft Agent Framework, not a Tracon guarantee. Read
the `custom` frame to decide whether the answer is complete.

The fix is a higher `ModelBinding.MaxOutputTokens`. When the binding leaves it empty,
`POST /api/agents/validate` warns with `output_truncation_risk` if the provider's
default is below the catalog's `MaxOutputTokens` for the model.

A model can also stop because the conversation and its answer filled the model's
**context window**. A provider reports that with
`TraconFinishReasons.ContextWindowExceeded` (Anthropic's
`model_context_window_exceeded`), and the outcome is the same as above, with one
difference: the error message and the event's `cause` (`context_window` instead of
`output_limit`) name the window. A higher output limit does not help there; a shorter
conversation (context compaction) or a model with a larger window does.

A job does not run a cut-off structured answer again: the next attempt sends the same
limit (see [Delivery and failure behavior](/guides/background-work/#delivery-and-failure-behavior)).

An online-evaluation judge asks for a structured answer too. When its answer is cut
off, the judge fails with `judge_failed` and the job does not retry it: the next
attempt would send the same limit. Raise the `MaxOutputTokens` of the judge's own
binding.

### Filtered output

A provider's content filter can stop a model in the middle of an answer: Anthropic
reports a `refusal`, Gemini a safety block. Tracon reads every provider's report as
one finish reason, and the outcome depends on what the model had written in the turn that ended the run:

| The final turn had written | Outcome |
|---|---|
| Nothing | The run fails with error type `content_filtered`, class `ContentFiltered`. Nothing is left to keep. A tool call, or text, from an earlier turn of the tool loop does not count: a filter that leaves the final turn empty fails the run even after tool calls |
| Text | The run completes with the partial answer and writes a `Custom` run event of type `tracon.output.filtered`. Its payload names the provider — never the answer text. The provider's own word for the filter is not in it |

A structured response (`Json` or `JsonSchema`) a filter stopped after it wrote text
gets the event too. The structured-response validator, when it is on, still decides
whether that document is usable. As with truncation, only the turn that ends the run
decides, and the event keeps its payload when `RecordToolPayloads` is off.

A client streaming the run receives the event as a `custom` frame right before `done`,
the way it receives `tracon.output.truncated`, with the same data the run's events
carry. No event, and no frame, is written while run recording is off
(`Tracon:RunRecording:Enabled`). On `/v1/responses` the same answer is `incomplete`
with the reason `content_filter` (see [OpenAI-compatible API](/guides/openai-api/)).

### The error message is safe to display, not safe to debug from

`error.message` is deliberately shallow. When the failure is Tracon's own —
a content filter, a blocked guard, a quota — the message is the same stable text
you'd write in a UI. When the failure comes from somewhere else (a provider SDK,
a webhook target, an MCP connection), the message carries only the exception's
type name and a correlation id, for example `HttpRequestException failed. (ref:
7f3a9c21)`: a foreign exception's own text can carry a request detail, an internal
address, or a partial credential, and none of that belongs in a persisted field or
an HTTP response. The full detail, matched to the same correlation id, goes to
your server's own log — that is where you debug a specific failure from, not from
the run record.

This applies everywhere a run, job, or webhook delivery can fail: the
`error`/`error_message` field on a run, job, or webhook delivery, the error
body of the HTTP, SSE, and MCP endpoints, and the `payload` of a workflow's
`ExecutorFailed` event all follow the same rule. One failure carries one
correlation id: the workflow's `ExecutorFailed` event and its run row name the
same log entry.

A failure the queue itself produces — rather than a handler — carries a stable
code instead of prose. A job queued for a handler key nobody registered fails
with `tracon.job.unknown-handler-key`, and the key itself is written to the
log rather than to `errorMessage`, for the same reason a foreign exception's
text is: the key may have come from an untrusted source, and that field is read
back over HTTP. A job whose worker died and whose attempt limit is used up fails
with `tracon.job.attempts-exhausted`. A queued run whose turn finished but whose
session could not be saved fails with `tracon.job.session-save-failed`; the model
is not called again. One whose turn asked for approval but whose approval could not be
written fails with `tracon.job.approval-save-failed`, and its run closes `Failed`. Match
on the code, never on the sentence around it.

## Read next

- [Runs and recording](/concepts/runs/) — what gets written, and when.
- [Starting runs and who they belong to](/concepts/starting-runs/) — attribution, authorization, and the ways a run starts.
- [Governance](/concepts/governance/) — what is guaranteed to be written.
