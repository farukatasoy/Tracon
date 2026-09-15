---
title: What Tracon is
description: What the packages give you, what they deliberately do not, and how to decide whether it fits.
slug: getting-started
sidebar:
  order: 1
---

Tracon is a **.NET package family** that adds a **control plane** on Microsoft
Agent Framework (MAF). MAF executes the agent and its model/tool loop. Tracon adds
agent definitions, configurable execution controls, HTTP endpoints, default-on
run recording, and an optional embedded console.

The packages run inside your host process. You choose the model providers,
persistence, identity integration, and operational policies. Recording is
best-effort: a recording-store failure is logged while agent execution continues.

:::note[Evaluate through the documentation]
Packages and templates are not published yet. You can explore the capabilities,
API contracts, and limitations here. Running the source requires authorized
repository access; the [source build guide](/getting-started/first-agent/) makes
that prerequisite explicit.
:::

## What you get

| | |
|---|---|
| **Definitions** | An agent as data: model, prompt, tools, skills, callable agents. Versioned, with rollback |
| **Runs** | Default-on recording — status, timings, tokens, cost, tool calls, traces, and an ordered event stream |
| **HTTP API** | 168 generated operations, plus OpenAI-compatible Responses and Chat Completions surfaces |
| **Console** | 30 screens embedded when you add `Tracon.UI` and call `UseUI()` |
| **Workflows** | Multi-agent execution with checkpoints and human-in-the-loop |
| **Evaluation** | Suites, cases, automatic judges, and A/B experiments between agent versions |
| **Governance** | Roles, scoped API keys, tenancy, approvals, guards, quotas, retention, webhooks, and audit |

See the [complete capability map](/capabilities/) for providers, testing,
RAG, voice, scheduling, external protocols, and production operations.

## What it deliberately is not

**It is not an abstraction over MAF.** `AIAgent`, `AgentSession`, `ChatMessage`, and
`AIFunction` are used directly and appear in the public API as themselves. There is
no parallel type hierarchy to learn and nothing between you and MAF's own extension
points.

**It is not a place to write code.** Tools are defined in your codebase and nowhere
else. An agent can be created and edited from the console, but tool *code* can never
be written through it — see [tools](/concepts/tools/) for the two narrow,
guarded exceptions.

**It is not a hosted service.** Tracon does not provide a managed hosting account.
Your host controls outbound connections: configured model providers, exporters,
webhooks, and integrations can send data outside the process. You can use a cloud
model API or a compatible self-hosted engine such as Ollama or vLLM — see
[picking a model provider](/packages/#picking-a-model-provider).

## Four rules it will not break

These conventions explain the default setup and the extension model.

1. **Explicit infrastructure.** `AddTracon()` supplies in-memory store defaults.
   Configure a model provider or a custom agent source for execution, and choose
   persistence when data must survive a restart.
2. **Tools are code only.** The console selects from registered tools; it never
   defines them.
3. **MAF objects are passed through, not wrapped.**
4. **Replaceable services.** Default service registrations use `TryAdd` so a
   consumer registration can supply the implementation. Follow each extension
   guide for its lifetime and registration contract.

## Is it for you?

It fits when you are building agents in .NET and want the operational layer without
building it: a record of what happened, a console for operators and teams that should
not need to modify application code, and a way to change supported agent
configuration without a deployment.

It does not fit if you want a hosted agent product, or if you are not on .NET. Runtime
packages target `net8.0`, `net9.0`, and `net10.0`, and the testing packages follow the
same matrix, so an app on .NET 8 LTS can be tested on .NET 8. The project template
generates a `net10.0` project, the `tracon` global tool targets `net10.0`, and the
source generator that ships inside Core targets `netstandard2.0`. The
[compatibility matrix](/reference/compatibility/) carries the per-package detail.

## Read next

- [Capability map](/capabilities/) — match a system requirement to a feature and its limits.
- [Architecture](/concepts/) — understand MAF and Tracon responsibilities.
- [Your first agent](/getting-started/first-agent/) — build a working host with repository access.
