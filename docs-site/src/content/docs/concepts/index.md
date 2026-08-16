---
title: Architecture
description: The layers, the dependency direction, and the four rules that explain most of the API.
slug: concepts
sidebar:
  order: 1
---

AgentPrism sits between your application and the Microsoft Agent Framework. It adds a
catalog, a compiler, a recording layer, an HTTP surface, and a console — and it adds
nothing between you and MAF's own types.

## The layers

```mermaid
flowchart TD
    APP["Your ASP.NET Core application"]
    UI["<b>AgentPrism.UI</b><br/>embedded React console"]
    HTTP["<b>AgentPrism.AspNetCore</b><br/>management API · OpenAI-compatible endpoints<br/>access layers · SSE"]
    PROV["<b>Providers</b><br/>OpenAI · Anthropic · Google · Azure · Voice"]
    STORE["<b>Persistence</b><br/>PostgreSQL · SQL Server · SQLite"]
    OPT["<b>Optional</b><br/>Workflows · MCP"]
    CORE["<b>AgentPrism.Core</b><br/>catalog · compiler · tool registry<br/>run recording · session manager · in-memory stores"]
    ABS["<b>AgentPrism.Abstractions</b><br/>contracts"]
    MAF["<b>Microsoft Agent Framework</b><br/>AIAgent · AgentSession · ChatClientAgent"]

    APP --> HTTP
    HTTP --> UI
    HTTP --> CORE
    PROV --> CORE
    STORE --> CORE
    OPT --> CORE
    CORE --> ABS --> MAF
```

The dependency direction is one-way and has no cycles: every provider and persistence
package points at `Core`, `Core` points at `Abstractions`, and `Abstractions` points
at MAF. Nothing points back. An architecture test enforces it, so a reference that
would break the picture fails the build rather than the review.

Workflows and MCP are the interesting case: they do **not** reference the HTTP layer,
and the HTTP layer reaches them only through abstractions. That is what keeps them
optional — without the workflow engine registered, the workflow *execution* endpoints
answer `501` while definition management keeps working.

## Four rules

Everything else follows from these.

### No surprises

`AddAgentPrism()` works alone. Without a configured database every store falls back to
memory. A developer installs the package, writes one line, and has a working console.
A database is never required, and neither is any particular model vendor — OpenAI,
Anthropic, Google, Azure OpenAI, and any OpenAI-compatible endpoint (including a
self-hosted engine like Ollama or vLLM) all work side by side.

### Tools are defined in code only

The console can create an agent; it can never write tool *code*. If it could, anyone
who reached the console could execute code on your server.

There are exactly two deliberate exceptions, both described in
[tools](/AgentPrism/concepts/tools/) with their guards: remote **MCP servers**, where
the process runs somewhere else and AgentPrism is only a client, and **skill
scripts**, where the process runs on this machine — the strictest exception, off by
default, behind six sequential gates. In both, a console user enables an existing
capability rather than writing new code. That distinction is the rule.

### MAF objects are passed through, not wrapped

`AIAgent`, `AgentSession`, `ChatMessage`, and `AIFunction` are used directly. No
parallel type hierarchy is laid on top of them.

Wrapping would create maintenance debt with every MAF release and cut you off from
the MAF ecosystem. AgentPrism is a *control plane*, not an *abstraction layer*.

### Every extension point is replaceable

All services register with `TryAdd`. Register your own implementation before calling
`AddAgentPrism()` and yours wins. The same holds for MAF's own hosting types, which is
why interfaces like conversation storage can be swapped out.

## Where things live

| | |
|---|---|
| Contracts, records, enums | `AgentPrism.Abstractions` |
| Catalog, compiler, recording, in-memory stores | `AgentPrism.Core` |
| Endpoints, access layers, OpenAI compatibility | `AgentPrism.AspNetCore` |
| Schema, migrations, vector search | `AgentPrism.PostgreSql` and friends |
| The console | `AgentPrism.UI` |

See [choosing packages](/AgentPrism/packages/) for which to install.

## Read next

- [Agents and definitions](/AgentPrism/concepts/agents/) — what an agent is here
- [Runs and recording](/AgentPrism/concepts/runs/) — what gets written, and when
- [Governance](/AgentPrism/concepts/governance/) — tenancy, audit, quotas, retention
