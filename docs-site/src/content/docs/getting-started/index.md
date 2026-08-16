---
title: What AgentPrism is
description: What the packages give you, what they deliberately do not, and how to decide whether it fits.
slug: getting-started
sidebar:
  order: 1
---

AgentPrism is a **control plane** for agents built with the Microsoft Agent
Framework (MAF). You bring the agents; it gives you the layer around them — a place
to define them, an HTTP API to drive them, a record of every run, and a console to
look at all of it.

It is a set of NuGet packages, not an application. It runs inside your ASP.NET Core
process, using your configuration, your authentication, and your database.

## What you get

| | |
|---|---|
| **Definitions** | An agent as data: model, prompt, tools, skills, callable agents. Versioned, with rollback |
| **Runs** | Default-on recording — status, timings, tokens, cost, tool calls, traces, and an ordered event stream |
| **HTTP API** | 143 generated operations, plus OpenAI-compatible Responses and Chat Completions surfaces |
| **Console** | 27 screens embedded when you add `AgentPrism.UI` and call `UseUI()` |
| **Workflows** | Multi-agent execution with checkpoints and human-in-the-loop |
| **Evaluation** | Suites, cases, automatic judges, and A/B experiments between agent versions |
| **Governance** | Roles, scoped API keys, tenancy, approvals, guards, quotas, retention, webhooks, and audit |

See the [complete capability map](/AgentPrism/capabilities/) for providers, testing,
RAG, voice, scheduling, external protocols, and production operations.

## What it deliberately is not

**It is not an abstraction over MAF.** `AIAgent`, `AgentSession`, `ChatMessage`, and
`AIFunction` are used directly and appear in the public API as themselves. There is
no parallel type hierarchy to learn and nothing between you and MAF's own extension
points.

**It is not a place to write code.** Tools are defined in your codebase and nowhere
else. An agent can be created and edited from the console, but tool *code* can never
be written through it — see [tools](/AgentPrism/concepts/tools/) for the two narrow,
guarded exceptions.

**It is not a hosted service.** There is no account, no telemetry leaving your
process, and no dependency on anything you do not run yourself. That includes the
model call itself: point a provider at a cloud API, or at a self-hosted engine such
as Ollama or vLLM on your own network — see [picking a model
provider](/AgentPrism/packages/#picking-a-model-provider).

## Four rules it will not break

These hold everywhere in the codebase, and knowing them explains most of the API.

1. **No surprises.** `AddAgentPrism()` works alone. Without a database every store
   falls back to memory, so the first thing you write runs without infrastructure.
2. **Tools are code only.** The console selects from registered tools; it never
   defines them.
3. **MAF objects are passed through, not wrapped.**
4. **Every extension point is replaceable.** Everything registers with `TryAdd`, so
   your own implementation registered first always wins.

## Is it for you?

It fits when you are building agents in .NET and want the operational layer without
building it: a record of what happened, a console for the people who did not write
the code, and a way to change an agent without a deployment.

It does not fit if you want a hosted agent product, or if you are not on .NET. Runtime
packages target `net8.0`, `net9.0`, and `net10.0`; the testing and template packages
require .NET 10, and the source generator that ships inside Core targets
`netstandard2.0`.

## Next

[Your first agent](/AgentPrism/getting-started/first-agent/) — a working application
in about five minutes.
