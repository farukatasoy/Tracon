---
title: The console
description: A tour of the 27 screens behind 33 routes — what each one is for and what it will not let you do.
slug: ui
---

The console ships inside `AgentPrism.UI` as a pre-built, Brotli-compressed
single-page application. There is no `node_modules` in your project and no JavaScript
build in your pipeline.

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UseUI();

app.MapAgentPrism("/agentprism");
```

There is no separate mapping call. `MapAgentPrism` finds the registration and binds
the console under the same prefix, so the prefix is written once.

:::note[Screenshots]
Every image on this page is produced by the end-to-end test run, in a real browser,
against a real server. The same run asserts each screen's landmark element, so a
screen that stops rendering fails the build instead of leaving a stale picture here.
:::

## Dashboard

![The dashboard: run counts, error rate, tokens, cost, and a runs-over-time chart](/AgentPrism/screenshots/dashboard.png)

Runs, errors, tokens, and cost across every agent, over a window you choose. Cost
appears only when pricing is configured; models with no pricing are counted
separately rather than silently written as zero.

The footer always states which storage is active. In the screenshot above it says
in-memory — a reminder that this data ends with the process.

## Agents

![The agent list, showing code-defined and database-defined agents](/AgentPrism/screenshots/agents.png)

Code-defined and database-defined agents in one list. The origin is on each entry,
because it decides what you can do: a code agent can be run and read but not edited,
and the console hides the edit form rather than offering one that would fail.

![An agent's detail screen with its model binding, tools, and version history](/AgentPrism/screenshots/agent-detail.png)

The detail screen carries the model binding, tools, skills, callable agents, and — for
a database agent — the version history with a diff between any two versions and a
one-click rollback.

The editor validates as you save, using the same rules the compiler applies. An
unknown tool or skill name is refused at write time, not on the first run.

## Playground

![The playground, with a streaming reply and tool calls rendered as cards](/AgentPrism/screenshots/playground.png)

Talk to an agent. The reply streams token by token and tool calls appear as cards with
their arguments and their results, so you can see *why* an answer came out the way it
did rather than just reading the answer.

Attachments can be added to a message, and with the voice layer enabled there is a
live conversation mode.

## Runs

![The run list with status, duration, token counts, and event counts](/AgentPrism/screenshots/runs.png)

Every execution, filterable by agent, status, and kind. Only root runs are shown by
default — otherwise a single question that fanned out to four agents would fill the
list with rows nobody started.

Opening a run gives the summary and the full event stream in order: message deltas,
tool calls with arguments and results, errors with their class. A run that called
other agents shows the whole tree, and each agent's tokens and duration are attributed
separately.

Two runs can be compared side by side, and any run can be scored — those scores sit
next to the ones automatic judges write.

## Workflows

![The workflow list and a compiled workflow graph](/AgentPrism/screenshots/workflows.png)

Workflows defined in code and in the database. Opening one draws the compiled graph,
and the node ids are the same executor ids that appear in run events — which is how
nodes light up live while a workflow runs.

A workflow waiting on human input shows the pending request as a card; answering it
resumes execution from the checkpoint as a new run.

## Evals and experiments

![The eval suite list](/AgentPrism/screenshots/evals.png)

Suites, their cases, and their past runs. A case can be promoted straight from a real
run, which is the fastest path from "this conversation went wrong" to "this is a
regression test".

![The experiment list with per-arm results](/AgentPrism/screenshots/experiments.png)

An experiment splits live traffic between two versions of the same agent. The results
show per-arm counts, error rates, tokens, and durations — and make no claim about a
winner. The numbers are yours to judge.

## Approvals

![Pending tool approvals awaiting a decision](/AgentPrism/screenshots/approvals.png)

Tool calls waiting on a human, with the arguments as they were recorded and an expiry.
Approving or rejecting both resume the run — the model has to see a result or a
refusal and carry on.

Standing "don't ask again" rules live under Governance, where they can be reviewed and
revoked.

## Catalog

![The tool list with each tool's JSON schema](/AgentPrism/screenshots/tools.png)

Tools with their generated JSON schemas — read-only, and permanently so. This screen
is where the code-only rule is most visible: you can see every tool an agent may use,
and there is no way to add one from here.

![Registered model providers and their catalogues](/AgentPrism/screenshots/models.png)

Providers and their configured models, with a health status served from cache.
A provider that implements no health check reports `Unknown`, which is not an error.
The MCP screen lists remote servers and the tools discovered from them.

## Governance

![The audit trail, filterable by actor, action, and entity](/AgentPrism/screenshots/audit.png)

Who changed what, when, and from what to what — filterable by actor, action, entity,
and date range. Secret-looking fields are masked before anything is stored.

Quotas, retention policies, API keys, tenants, and skill script grants have their own
screens in the same area.

![The diagnostics screen showing storage, migrations, and configuration](/AgentPrism/screenshots/diagnostics.png)

Diagnostics answers "is this deployment actually wired up the way I think": which
stores are active, whether migrations are up to date, and what is pending.

## Settings

![Settings: version, prefix, authentication method, active stores, theme, language](/AgentPrism/screenshots/settings.png)

Version, prefix, authentication method, active stores, theme, and language.

## Things worth knowing

- The console works under **any** prefix and learns it at run time
- Light and dark themes; the default follows the operating system
- English and Turkish; the default follows the browser. Server messages are shown as
  they are and never translated — the same failure has to read the same way in a log,
  a test, and a support ticket
- A command palette on `⌘K` / `Ctrl+K`
- Buttons the caller has no role for are hidden, based on what `/api/meta` reports
- The token is kept in `sessionStorage` and is gone when the tab closes; theme and
  language live in `localStorage`
- The JavaScript budget is 250 KB gzip and is enforced by the build. Current size:
  165.8 KB

## Read next

- [Securing the endpoints](/AgentPrism/getting-started/security/) — why the shell is
  exempt from the bearer layer
- [The HTTP API](/AgentPrism/http-api/) — everything the console does, as requests
