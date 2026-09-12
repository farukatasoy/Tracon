---
title: Glossary
description: Precise definitions for the Tracon terms used across agent design, execution, storage, security, evaluation, and operations.
slug: reference/glossary
---

Agent systems reuse familiar words for different things. These definitions state what
each term means inside Tracon.

## Agents and context

**Agent.** A MAF `AIAgent` resolved through the Tracon catalog. It can come from a
  declarative definition or a factory in code.

**Agent definition.** Data that names an agent and selects its instructions, model binding, tools,
  skills, callable agents, context features, and metadata. The compiler validates a
  definition before it can resolve.

**Code-defined agent.** An agent registered by application code. It is visible and runnable through the
  API and console, but it is not editable there. It wins a same-name collision.

**Database-defined agent.** A versioned definition managed through the store, HTTP API, or console. Each save
  appends a version. Rollback also appends a new version.

**Catalog.** The tenant-aware view that resolves agents, tools, skills, model providers, and
  related descriptors from all registered sources.

**Definition compiler.** The component that turns an `AgentDefinition` into a runnable `AIAgent`. It checks
  references, provider settings, response format, and the agent-call graph first.

**Model binding.** The provider name, model or deployment name, generation settings, provider-specific
  settings, and optional response format selected by an agent.

**Provider.** An `IModelProvider` that creates model clients and can publish a model catalog and
  health check. A provider name is stable configuration data.

**Model catalog.** A configured list used by the UI and pricing resolver. It is informative. It is
  not an allowlist, and an unlisted model can still work.

**Structured output.** An explicit response contract: plain text, any valid JSON document, or JSON that
  follows a supplied JSON Schema.

**Harness.** An optional agent runtime that adds bounded iteration and context behavior, such as
  todo, file memory, web search, skills, and mode providers.

**Compaction.** Reducing conversation context after a configured message, turn, or token trigger.
  A strategy can preserve recent groups and summarize older content with another
  model.

**Memory.** Agent working-context features such as todo state, file memory, text search, and
  vector search. It is not the same as a durable conversation session.

## Tools, skills, and protocols

**Tool.** A registered `AIFunction` that a model may call. Executable tool behavior comes from
  application code or a deliberately gated skill script, not from a free-form UI
  field.

**Generated tool.** A method marked with `[TraconTool]` and registered by generated code. It avoids
  runtime reflection and dynamic-code requirements.

**Tool approval.** A required human or standing-rule decision before a sensitive tool call executes.
  A decision becomes part of run history.

**Standing approval rule.** A revocable policy that pre-approves a tool, optionally only for one exact argument
  set. It does not make an unregistered tool available.

**Skill.** Named Markdown instructions with optional resources and scripts. A skill changes
  agent context; it is not a model provider or a remote MCP server.

**Skill script.** Executable content attached to a skill. It needs feature enablement, a platform
  isolation acknowledgement, an allowed interpreter, and a tenant grant.

**Content guard.** An input or output inspector that can allow, mask, or block content. Multiple guards
  run in sequence and the strictest decision wins.

**MCP client.** The `Tracon.Mcp` capability that discovers tools, prompts, and resources from
  remote HTTP Model Context Protocol servers.

**MCP server.** The optional ASP.NET Core surface that publishes an explicit set of Tracon
  agents as MCP tools for external clients.

**A2A.** The agent-to-agent protocol surface that publishes an explicit allowlist of agents.
  It is separately registered, separately mapped, and protected by `ExternalInvoke`.

**Knowledge collection.** A tenant-scoped group of document chunks and embeddings. PostgreSQL performs cosine
  vector search over it; the collection name also selects an agent's search tool.

**Embedding generator.** The consumer-registered `IEmbeddingGenerator<string, Embedding<float>>` that turns
  knowledge text and search queries into vectors.

## Execution and state

**Run.** One agent or workflow execution. A run has an id, status, timing, usage, optional
  cost, error classification, and ordered events when recording succeeds.

**Root run.** The execution a caller started. Child-agent and workflow work can create descendant
  runs that share its root id and budget.

**Child run.** A run created when one agent invokes another. It records its own usage and duration
  while remaining part of the root run tree.

**Run event.** One ordered fact in an execution, such as start, text delta, tool invocation,
  completed message, failure, or completion.

**Event stream.** The gapless sequence of run events. HTTP clients receive it with SSE and can resume
  from a sequence id.

**Session.** Durable conversation identity and provider state across runs. A session is not a
  run; several runs can continue the same session.

**Conversation item.** An addressable message or tool-related item in readable session history. SQL-backed
  items allow branching.

**Branch.** A new session whose history begins from a selected item in another durable
  conversation. Existing history is never rewritten.

**Attachment.** Stored binary or text content referenced by a message. The upload guard checks size,
  allowed media type, and file signature.

**Replay.** Starting a new run from recorded input. Replay can reuse recorded tool results or
  call tools again, subject to its selected mode and mismatch checks.

**Idempotency key.** A caller-supplied key that binds a tenant, operation, request hash, and stored
  response. A matching retry returns the prior response instead of repeating work.

**Cancellation request.** A signal sent to a currently registered run. It asks execution to stop and lets the
  recording pipeline close the run with the resulting state.

## Workflows and background work

**Workflow.** A compiled MAF execution graph. Database workflows compose supported patterns;
  code workflows can build custom executors and edges.

**Checkpoint.** Durable workflow state written at a resumable boundary. It lets another process
  continue without replaying completed steps.

**Human-input request.** A typed question that pauses a workflow. A response resumes execution from its
  checkpoint rather than changing the old run.

**Job.** Durable background work with a kind, payload items, lease, attempts, state, and
  handler. Async runs, evals, retention, webhooks, and other services use jobs.

**Schedule.** A one-time or cron rule that enqueues a job. Its time zone and next run are stored
  explicitly.

**Lease.** A time-bounded claim that lets one worker own a job or singleton responsibility.
  Expiry allows another healthy process to recover it.

**Singleton execution.** Distributed lease coordination for services that must have one active executor
  across application nodes.

**Heartbeat.** A periodic update from a running operation that proves its owning process is alive.

**Run reconciliation.** A scanner that compares heartbeats with an orphan threshold and closes abandoned
  runs after process loss.

## Evaluation and change

**Eval suite.** A named set of cases, checks, and past eval runs used to detect behavior regressions.

**Eval case.** One repeatable input plus its deterministic or custom checks. A production run can
  be promoted into a case.

**Judge.** An `IRunJudge` that assigns named scores to a completed run. It can be deterministic
  or model-backed. A judge is a singleton and can receive concurrent calls.

**Online evaluation.** Bounded background scoring of a configured sample of live runs. It is off until a
  judge, enable flag, and positive sample rate all exist.

**Experiment.** Stable traffic assignment between versions of the same agent with per-arm counts,
  errors, usage, and duration. Tracon reports results but does not declare a
  winner.

**Canary.** An experiment arm used for limited rollout. An optional policy can stop or roll it
  back when configured evidence crosses a threshold.

## Security, governance, and operations

**Tenant.** The top-level data and authority partition. Stores and requests carry a tenant id;
  single-tenant mode uses the configured default id.

**Role.** An ASP.NET Core authorization policy such as Reader, Operator, or Admin. Tracon
  does not store users or role assignments.

**API-key scope.** One closed enum value that narrows what a key may do. Effective authority is the
  intersection of role and scopes.

**External surface.** A separately exposed MCP-server or A2A route. It needs the `ExternalInvoke` scope
  and additional exposure safeguards.

**Quota.** A tenant or agent usage rule checked before a run. An enabled subsystem with no rule
  rejects nothing.

**Rate limit.** A fixed-window HTTP admission limit partitioned by tenant, API key, or remote
  address. It is separate from spend and token quotas.

**Audit record.** An append-only administrative change with actor, action, entity, and masked
  before/after data.

**Webhook.** A signed outbound event delivery. The sender applies SSRF controls, response limits,
  retries, and automatic disablement after repeated failure.

**Retention policy.** An age or row rule that selects stored operational data for preview, archive, and
  background deletion.

**Archive.** An `IArchiveSink` destination used before retention deletes selected rows. Archive
  is a policy choice, not a default backup system.

**Trace.** The full OpenTelemetry activity tree for an operation.

**Span.** One timed activity inside a trace, such as a model call, tool call, or agent run.

**Metric.** An aggregated .NET measurement for rates, duration, usage, cost, errors, tools,
  judges, background jobs, or the optional quota and queue-depth gauges.

**Control plane.** The catalog, execution records, policies, operational services, API, and console
  around agents. Tracon adds this plane without hiding the MAF objects below it.

**Console.** The embedded Tracon UI. It is an operator client of the same management API
  that external automation can call.

## Read next

- [Architecture](/concepts/) — the same terms as a system, not a list
- [Complete capability map](/capabilities/) — the feature each term belongs to
- [Configuration](/reference/configuration/) — the option names these terms appear in
