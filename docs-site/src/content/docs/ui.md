---
title: The console
description: Tour the embedded Tracon console by task, understand its security model, and know which features remain code or HTTP only.
slug: ui
---

The console ships inside `Tracon.UI` as a pre-built, Brotli-compressed
single-page application. There is no `node_modules` in your project and no JavaScript
build in your pipeline.

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .UseUI();

app.MapTracon("/tracon");
```

There is no separate mapping call. `MapTracon` finds the registration and binds
the console under the same prefix, so the prefix is written once.

:::note[Screenshots]
Every image on this page is produced by the end-to-end test run, in a real browser,
against a real server. The same run asserts each screen's landmark element, so a
screen that stops rendering fails the build instead of leaving a stale picture here.
:::

## How it is laid out

The console is built for an operator who leaves it open, not for a visitor who
reads it once. Dark is the default palette, the base text size is 13 px, and a
table row is about 32 px tall.

Navigation is split by the job you are doing. **Operate** holds what the airspace
is doing right now — dashboard, playground, runs, sessions, approvals, jobs,
evaluations, audit, diagnostics. **Configure** holds what is allowed to fly —
agents, workflows, tools, skills, models, MCP servers, triggers, settings.

There is one accent colour. Everything else that is coloured is a status, and the
same status is the same colour on every screen:

| Reads as | Means |
|---|---|
| Accent | In flight now — a run streaming, the row you are on |
| Green | Cleared — completed, healthy, approved |
| Amber | Holding — queued, waiting on a person, degraded |
| Red | Denied — failed, rejected, unhealthy |
| Blue | Contact — something started or was attached |
| Grey | Standby — cancelled, idle, structural |

Colour is never the only signal: every status also carries its own word, and
charts use a separate series palette so a line is never mistaken for a verdict.
Both palettes are measured against WCAG AA on every build — text at 5.2:1 or
better, borders and graphics at 3.5:1 or better, in both themes.

Every identifier — a run, a session, a tenant, a span, an API key prefix — is
monospace at one size, so two of them can be compared by shape. The ones worth
moving into a query or a ticket carry a copy control.

## Dashboard

<a class="ui-shot" href="/screenshots/dashboard.png"><img src="/screenshots/dashboard.png" alt="The dashboard: run counts, error rate, tokens, cost, and a runs-over-time chart" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Runs, errors, tokens, and cost across every agent, over a window you choose. Cost
appears only when pricing is configured; models with no pricing are counted
separately rather than silently written as zero.

A **token breakdown** bar shows where the tokens actually went: prompt-cache hits,
fresh input, reasoning, and plain output. The four slices are disjoint — cache hits
and reasoning are re-cut out of the input and output totals rather than added
beside them, so the bar is never longer than the tokens that were spent. Each slice
is named with its own count underneath, so colour is never the only signal.

The footer always states which storage is active. In the screenshot above it says
in-memory — a reminder that this data ends with the process.

## Agents

<a class="ui-shot" href="/screenshots/agents.png"><img src="/screenshots/agents.png" alt="The agent list, showing code-defined and database-defined agents" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Code-defined and database-defined agents in one list. The origin is on each entry,
because it decides what you can do: a code agent can be run and read but not edited,
and the console hides the edit form rather than offering one that would fail.

<a class="ui-shot" href="/screenshots/agent-detail.png"><img src="/screenshots/agent-detail.png" alt="An agent's detail screen with its model binding, tools, and version history" width="2880" height="1800" loading="lazy" decoding="async" /></a>

The detail screen carries the model binding, tools, skills, callable agents, and — for
a database agent — the version history with a diff between any two versions and a
one-click rollback.

The editor validates as you save, using the same rules the compiler applies. An
unknown tool or skill name is refused at write time, not on the first run.

The instructions panel also holds a culture-keyed section: add a culture tag
(`tr`, `de`, ...) with its own instructions text, and a run requesting that
culture picks it up (see [Culture-keyed instructions](/concepts/agents/#culture-keyed-instructions)).
The version diff screen shows each culture's text as its own section.

## Playground

<a class="ui-shot" href="/screenshots/playground.png"><img src="/screenshots/playground.png" alt="The playground, with a streaming reply and tool calls rendered as cards" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Talk to an agent. The reply streams token by token and tool calls appear as cards with
their arguments and their results, so you can see *why* an answer came out the way it
did rather than just reading the answer.

Attachments can be added to a message, and with the voice layer enabled there is a
live conversation mode.

When the selected agent declares a parameter schema, the playground renders a form
for it above the message box — one field per parameter, generated from the schema.
The Run button is disabled while a required field is empty; this is the same rule
the server enforces (a run with a missing required value never starts), surfaced
before the round trip instead of after it.

A tool call that needs a human decision stops the stream and renders as an approval
card instead of a result — the entity name and message from a registered
[approval presenter](/concepts/governance/#approvals) when there is one, the raw
call arguments otherwise. Approving or rejecting resumes the run as a new turn.

## Runs

<a class="ui-shot" href="/screenshots/runs.png"><img src="/screenshots/runs.png" alt="The run list with status, duration, token counts, and event counts" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Every execution, filterable by agent, status, and kind — and, once your application
binds `IRunAttributionContext`, by **user** and by **label** (`key:value`, or a bare
key to match any value of it). Only root runs are shown by default — otherwise a
single question that fanned out to four agents would fill the list with rows nobody
started.

A run's detail header names the user it belongs to and shows its labels as badges.

Opening a run gives the summary and the full event stream in order: message deltas,
tool calls with arguments and results, errors with their class. The summary names
the provider that actually answered, next to the model, and — when pricing is
configured — the unit price applied for input, output, and any cached input; that
price is a snapshot of what was in effect when the run ended, unaffected by a later
catalog or configuration change. When a reasoning
model's thinking is recorded, it renders as its own collapsible block, separate from
the answer. A run that called other agents shows the whole tree, and each agent's
tokens and duration are attributed separately. A sub-agent call that passed its
wait limit shows a distinct timeline entry naming which layer cut it — see
[Agents calling agents](/concepts/agents/#agents-calling-agents). A tool that writes its own custom
event — see [Writing your own event](/concepts/runs/#writing-your-own-event) —
renders as a generic card named after that event's own type, with no console
change required to show up.

Two runs can be compared side by side, and any run can be scored. The thumbs on the
run screen write one named score — `overall` — as the person looking at it. Every
other score on that run, whether a second name someone wrote through the API or one
an automatic judge produced, is listed beside them read-only, with its name and its
value.

A run that continues one interrupted by a process crash names the run it continues,
right next to the session and parent-run links, as a clickable id that opens the
source run.

## Sessions

<a class="ui-shot" href="/screenshots/sessions.png"><img src="/screenshots/sessions.png" alt="The session list with message counts and last activity" width="2880" height="1800" loading="lazy" decoding="async" /></a>

A session is a durable conversation. The list shows every session the active store
knows about, with its agent, message count, and last activity; opening one reads the
conversation back turn by turn, exactly as the model saw it.

Two things are only visible here. A session can be **branched** from any addressable
item, which forks the conversation without touching the original — the fork opens as
a new session with its own id. And attachments referenced by a message are listed with
their type and size, so you can see what actually reached the provider.

In-memory storage keeps sessions only for the life of the process. Reading a
conversation back, and branching it, need a SQL store.

## Jobs

<a class="ui-shot" href="/screenshots/jobs.png"><img src="/screenshots/jobs.png" alt="The job queue with handler key, status, attempt count, and next run time" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Everything Tracon runs in the background, in one queue: queued agent runs,
scheduled runs, workflow executions, evaluation runs, online-evaluation scoring, and
webhook deliveries. Each row carries its handler key, status, attempt count, and —
for a failure — the classified error. The handler key is what decides which code
runs the job, so it is also the most useful thing to filter the queue by.

Schedules live on the same screen. A schedule is a cron expression, a handler key,
and the payload to run; leaving the expression empty makes it manual-only, which is
the honest way to park one. Triggering a schedule by hand queues exactly the job the
timer would have. The handler dropdown lists only the keys this server accepts over
HTTP, so a schedule the form lets you save is one the server will take.

Every job and schedule shows its lane — a plain text tag that keeps unrelated kinds
of work from blocking each other in the same queue. Filter the job list by lane to
find work a worker is not currently scoped to pick up.

The queue drains in any process whose worker is running. That is the default:
`TraconSchedulingOptions.Enabled` and `RunWorker` are both `true`, so calling
`UseScheduling()` is about configuring the worker, not switching it on. A queue
that never moves is almost always a deployment where every process sets
`RunWorker` to `false` — see [Jobs, schedules, and queues](/guides/background-work/).

## Workflows

<a class="ui-shot" href="/screenshots/workflows.png"><img src="/screenshots/workflows.png" alt="The workflow list and a compiled workflow graph" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Workflows defined in code and in the database. Opening one draws the compiled graph,
and the node ids are the same executor ids that appear in run events — which is how
nodes light up live while a workflow runs.

A workflow waiting on human input shows the pending request as a card; answering it
resumes execution from the checkpoint as a new run.

## Evals and experiments

<a class="ui-shot" href="/screenshots/evals.png"><img src="/screenshots/evals.png" alt="The eval suite list" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Suites, their cases, and their past runs. A case can be promoted straight from a real
run, which is the fastest path from "this conversation went wrong" to "this is a
regression test".

<a class="ui-shot" href="/screenshots/experiments.png"><img src="/screenshots/experiments.png" alt="The experiment list with per-arm results" width="2880" height="1800" loading="lazy" decoding="async" /></a>

An experiment splits live traffic between two versions of the same agent. The results
show per-arm counts, error rates, tokens, and durations — and make no claim about a
winner. The numbers are yours to judge.

## Approvals

<a class="ui-shot" href="/screenshots/approvals.png"><img src="/screenshots/approvals.png" alt="Pending tool approvals awaiting a decision" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Tool calls waiting on a human, with the arguments as they were recorded and an expiry.
When a registered [approval presenter](/concepts/governance/#approvals) resolves an
entity name and message for the call, the table shows that instead of the raw
arguments; a call the presenter could not resolve still shows the arguments, since
the presenter fails open. Approving or rejecting both resume the run — the model has
to see a result or a refusal and carry on.

Standing "don't ask again" rules live under Governance, where they can be reviewed,
revoked, or written by hand with an argument condition (for example `amount <= 100`)
instead of an exact-argument fingerprint. The condition editor is a closed set of
comparisons, not a free-text expression box.

## Catalog

### Tools

<a class="ui-shot" href="/screenshots/tools.png"><img src="/screenshots/tools.png" alt="The tool list with each tool's JSON schema" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Tools with their generated JSON schemas — read-only, and permanently so. This screen
is where the code-only rule is most visible: you can see every tool an agent may use,
and there is no way to add one from here. A tool registered with `AddClientTool(...)`
carries a "client-side" badge: its declaration is still code-only, but its body runs
on the caller instead of the server. See
[Client-side tools and the embeddable widget](/guides/client-side-tools/).
A destructive tool carries a red badge, one that sends data outside the process an
orange one, and a tool with a declared permission or a non-default timeout shows both
next to it — see [Tools: authorization and timeout](/concepts/tools/#authorization-validation-and-timeout).

### Skills

<a class="ui-shot" href="/screenshots/skills.png"><img src="/screenshots/skills.png" alt="The skill list with frontmatter, resources, and allowed tools" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Skills sit beside the tools. A skill is markdown instructions plus read-only resources
that an agent loads at run time, with approval. The editor shows the frontmatter, the
compatibility and license fields, the allowed-tool list, and the resources; the
markdown is stored as source text and the console does not render it.

This is the second place the code-only boundary is visible: a skill may carry a
**script**, but the console can only reference a script the application already
registered and granted. It cannot write one.

### Models

<a class="ui-shot" href="/screenshots/models.png"><img src="/screenshots/models.png" alt="Registered model providers and their catalogues" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Providers and their configured models, with a health status served from cache.
A provider that implements no health check reports `Unknown`, which is not an error.
### MCP

<a class="ui-shot" href="/screenshots/mcp.png"><img src="/screenshots/mcp.png" alt="Registered MCP servers and the tools discovered from them" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Remote MCP servers and the tools discovered from each. A server is a definition — an
endpoint, a transport, and the name of the configuration key its authorization value is
read from — so the console never holds a credential. Discovered tools carry the same
approval badges as code-defined ones, and refreshing a server re-reads its catalog
without a restart.

## Governance

### Audit

<a class="ui-shot" href="/screenshots/audit.png"><img src="/screenshots/audit.png" alt="The audit trail, filterable by actor, action, and entity" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Who changed what, when, and from what to what — filterable by actor, action, entity,
and date range. Secret-looking fields are masked before anything is stored.

Quotas, retention policies, API keys, tenants, tenant provider bindings and egress
policy (BYOK), and skill script grants have their own screens in the same area.

### Triggers

<a class="ui-shot" href="/screenshots/triggers.png"><img src="/screenshots/triggers.png" alt="Inbound triggers with their target, payload mode, and signed-request URL" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Inbound triggers let an external system start a queued run with one signed request and
no API key. A trigger's editor shows the exact URL to configure in that system, the
target agent or workflow, and the name of the configuration key holding its signing
secret — see [Inbound triggers](/guides/inbound-triggers/).

### Diagnostics

<a class="ui-shot" href="/screenshots/diagnostics.png"><img src="/screenshots/diagnostics.png" alt="The diagnostics screen showing storage, migrations, and configuration" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Diagnostics answers "is this deployment actually wired up the way I think": which
stores are active, whether migrations are up to date, and what is pending.

It is opt-in because setup details are sensitive. Enable the Admin endpoint before
the screen can load it:

```csharp
app.MapTracon("/tracon", options =>
{
    options.EnableDiagnosticsEndpoint = true;
});
```

## Settings

<a class="ui-shot" href="/screenshots/settings.png"><img src="/screenshots/settings.png" alt="Settings: version, prefix, authentication method, active stores, theme, language" width="2880" height="1800" loading="lazy" decoding="async" /></a>

Version, prefix, authentication method, active stores, theme, and language.

## Embeddable chat widget

`Tracon.UI` also builds a second, much smaller bundle: a floating chat widget
meant for a **different** page — your own product's site, not the console. It ships
from a separate Vite entry, with its own budget gate (30 KB gzip; current size:
2.7 KB), so console code cannot leak into it. `UseUI()` serves both; no separate
registration is needed.

```html
<script src="https://your-tracon-host/tracon/embed/embed.js"
  data-server="https://your-tracon-host"
  data-agent="support"
  data-api-key="sk_..."></script>
```

The widget calls the run endpoint directly from the embedding page's origin, so
[`AllowedOrigins`](/reference/configuration/) must list that origin —
empty by default, so a page you have not explicitly allowed is blocked by the
browser. See
[Client-side tools and the embeddable widget](/guides/client-side-tools/)
for the full walkthrough, including how the widget runs a client-side tool.

## Things worth knowing

- The console works under **any** prefix and learns it at run time
- Dark and light themes. A fresh console opens dark; Settings also offers
  "follow system", which then tracks the operating system
- English and Turkish; the default follows the browser. Server messages are shown as
  they are and never translated — the same failure has to read the same way in a log,
  a test, and a support ticket
- A command palette on `⌘K` / `Ctrl+K` reaches every screen, every recent run and
  session, and the few actions that are not a navigation
- The first `Tab` on any screen is "Skip to content". Every layer closes with
  `Esc` and hands focus back to whatever opened it
- Buttons the caller has no role for are hidden, based on what `/api/meta` reports
- The token is kept in `sessionStorage` and is gone when the tab closes; theme and
  language live in `localStorage`
- The JavaScript budget is 250 KB gzip and is enforced by the build. Current size:
  184.1 KB
- Four run-time JavaScript dependencies, and the build fails on a fifth. Dialogs,
  menus, tooltips and the command palette are written against the platform rather
  than pulled from a component library

### Editor boundaries

The editor supports structured output, harness settings, and compaction. Three newer
definition areas are code/HTTP-only today: provider-specific `ProviderSettings`,
static `McpResourceUris`, and Knowledge vector bindings. Do not round-trip a
definition that uses those fields through the current editor; edit it through code or
the management HTTP API so the fields remain explicit.

## Read next

- [Securing the endpoints](/getting-started/security/) — why the shell is
  exempt from the bearer layer
- [The HTTP API](/http-api/) — everything the console does, as requests
- [Client-side tools and the embeddable widget](/guides/client-side-tools/) — let a client execute approved tools through the widget contract.
  — a tool whose body runs in the browser, and the chat widget that runs it
