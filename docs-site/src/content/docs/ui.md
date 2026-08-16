---
title: The console
description: Tour the embedded AgentPrism console by task, understand its security model, and know which features remain code or HTTP only.
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

<a class="ui-shot" href="/AgentPrism/screenshots/dashboard.png"><img src="/AgentPrism/screenshots/dashboard.png" alt="The dashboard: run counts, error rate, tokens, cost, and a runs-over-time chart" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Runs, errors, tokens, and cost across every agent, over a window you choose. Cost
appears only when pricing is configured; models with no pricing are counted
separately rather than silently written as zero.

The footer always states which storage is active. In the screenshot above it says
in-memory — a reminder that this data ends with the process.

## Agents

<a class="ui-shot" href="/AgentPrism/screenshots/agents.png"><img src="/AgentPrism/screenshots/agents.png" alt="The agent list, showing code-defined and database-defined agents" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Code-defined and database-defined agents in one list. The origin is on each entry,
because it decides what you can do: a code agent can be run and read but not edited,
and the console hides the edit form rather than offering one that would fail.

<a class="ui-shot" href="/AgentPrism/screenshots/agent-detail.png"><img src="/AgentPrism/screenshots/agent-detail.png" alt="An agent's detail screen with its model binding, tools, and version history" width="2880" height="1800" loading="lazy" decoding="async" /></a>

The detail screen carries the model binding, tools, skills, callable agents, and — for
a database agent — the version history with a diff between any two versions and a
one-click rollback.

The editor validates as you save, using the same rules the compiler applies. An
unknown tool or skill name is refused at write time, not on the first run.

## Playground

<a class="ui-shot" href="/AgentPrism/screenshots/playground.png"><img src="/AgentPrism/screenshots/playground.png" alt="The playground, with a streaming reply and tool calls rendered as cards" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Talk to an agent. The reply streams token by token and tool calls appear as cards with
their arguments and their results, so you can see *why* an answer came out the way it
did rather than just reading the answer.

Attachments can be added to a message, and with the voice layer enabled there is a
live conversation mode.

## Runs

<a class="ui-shot" href="/AgentPrism/screenshots/runs.png"><img src="/AgentPrism/screenshots/runs.png" alt="The run list with status, duration, token counts, and event counts" width="2880" height="1800" loading="lazy" decoding="async" /></a>

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

<a class="ui-shot" href="/AgentPrism/screenshots/workflows.png"><img src="/AgentPrism/screenshots/workflows.png" alt="The workflow list and a compiled workflow graph" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Workflows defined in code and in the database. Opening one draws the compiled graph,
and the node ids are the same executor ids that appear in run events — which is how
nodes light up live while a workflow runs.

A workflow waiting on human input shows the pending request as a card; answering it
resumes execution from the checkpoint as a new run.

## Evals and experiments

<a class="ui-shot" href="/AgentPrism/screenshots/evals.png"><img src="/AgentPrism/screenshots/evals.png" alt="The eval suite list" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Suites, their cases, and their past runs. A case can be promoted straight from a real
run, which is the fastest path from "this conversation went wrong" to "this is a
regression test".

<a class="ui-shot" href="/AgentPrism/screenshots/experiments.png"><img src="/AgentPrism/screenshots/experiments.png" alt="The experiment list with per-arm results" width="2880" height="1800" loading="lazy" decoding="async" /></a>

An experiment splits live traffic between two versions of the same agent. The results
show per-arm counts, error rates, tokens, and durations — and make no claim about a
winner. The numbers are yours to judge.

## Approvals

<a class="ui-shot" href="/AgentPrism/screenshots/approvals.png"><img src="/AgentPrism/screenshots/approvals.png" alt="Pending tool approvals awaiting a decision" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Tool calls waiting on a human, with the arguments as they were recorded and an expiry.
Approving or rejecting both resume the run — the model has to see a result or a
refusal and carry on.

Standing "don't ask again" rules live under Governance, where they can be reviewed and
revoked.

## Catalog

<a class="ui-shot" href="/AgentPrism/screenshots/tools.png"><img src="/AgentPrism/screenshots/tools.png" alt="The tool list with each tool's JSON schema" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Tools with their generated JSON schemas — read-only, and permanently so. This screen
is where the code-only rule is most visible: you can see every tool an agent may use,
and there is no way to add one from here.

<a class="ui-shot" href="/AgentPrism/screenshots/models.png"><img src="/AgentPrism/screenshots/models.png" alt="Registered model providers and their catalogues" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Providers and their configured models, with a health status served from cache.
A provider that implements no health check reports `Unknown`, which is not an error.
The MCP screen lists remote servers and the tools discovered from them.

## Governance

<a class="ui-shot" href="/AgentPrism/screenshots/audit.png"><img src="/AgentPrism/screenshots/audit.png" alt="The audit trail, filterable by actor, action, and entity" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Who changed what, when, and from what to what — filterable by actor, action, entity,
and date range. Secret-looking fields are masked before anything is stored.

Quotas, retention policies, API keys, tenants, and skill script grants have their own
screens in the same area.

<a class="ui-shot" href="/AgentPrism/screenshots/diagnostics.png"><img src="/AgentPrism/screenshots/diagnostics.png" alt="The diagnostics screen showing storage, migrations, and configuration" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Diagnostics answers "is this deployment actually wired up the way I think": which
stores are active, whether migrations are up to date, and what is pending.

It is opt-in because setup details are sensitive. Enable the Admin endpoint before
the screen can load it:

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.EnableDiagnosticsEndpoint = true;
});
```

## Settings

<a class="ui-shot" href="/AgentPrism/screenshots/settings.png"><img src="/AgentPrism/screenshots/settings.png" alt="Settings: version, prefix, authentication method, active stores, theme, language" width="2880" height="1800" loading="lazy" decoding="async" /></a>

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
  165.4 KB

### Editor boundaries

The editor supports structured output, harness settings, and compaction. Three newer
definition areas are code/HTTP-only today: provider-specific `ProviderSettings`,
static `McpResourceUris`, and Knowledge vector bindings. Do not round-trip a
definition that uses those fields through the current editor; edit it through code or
the management HTTP API so the fields remain explicit.

## Read next

- [Securing the endpoints](/AgentPrism/getting-started/security/) — why the shell is
  exempt from the bearer layer
- [The HTTP API](/AgentPrism/http-api/) — everything the console does, as requests
