# AgentPrism

**A production-grade agent control plane for the Microsoft Agent Framework.**

AgentPrism is a .NET package family built on
[Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/).
You write the AI harness; you operate it at `/agentprism`.

> **Status:** preview. AgentPrism is **operable**, its
> [product documentation is published](https://farukatasoy.github.io/AgentPrism), and the
> **public API gate** (`EnablePublicApiTracking`) is on independently of any release
> decision — an unrecorded surface change breaks the build. Start with
> `dotnet new agentprism-api` and test without calling a model using
> `AgentPrism.Testing`. Runs are recorded with spans, metrics, and cost; tenants are
> isolated; agents can be exposed over MCP and A2A; `pgvector` powers semantic search;
> API keys narrow access; A/B experiments roll back automatically under a canary rule.
> A tool body can run in the browser (`AddClientTool`), and an embeddable chat widget
> can be served to third-party pages over CORS. The audit trail is made tamper-evident
> with a hash chain (`GET /api/audit/verify`), and a data subject's content can be
> exported and erased by identity (`IDataSubjectResolver`). A tenant can bring its own
> model provider key (BYOK), and an egress policy limits which providers its agents may
> reach — an unauthorised provider is rejected at compile time. An external system such
> as Slack can start a queued agent or workflow run with one signed HTTP request and no
> API key (`POST /api/triggers/{tenantId}/{name}`). The **coding agent** integrating the
> package learns from build-time diagnostics and an opt-in `AGENTS.md` capability map.

```csharp
builder.AddAgentPrism()
       .UsePostgreSql(connectionString)
       .UseOpenAI(apiKey)
       .AddTool(GetOrderStatus)
       .UseUI();

app.MapAgentPrism("/agentprism");
```

Two lines: a working agent, durable sessions, and a control plane at
`http://localhost:5080/agentprism`.

### The console

Dashboard, Agents, Skills, Playground, Sessions, Runs, Workflows, Jobs, Evals,
Experiments, Approvals, Tools, Models, MCP, Triggers, Audit, Diagnostics, Settings —
**28 screens across 36 routes**, lists and editors included.

Written in React 19 and TypeScript, built with Vite, and embedded in the assembly
**Brotli-compressed**. No JavaScript dependency appears in the consuming project and no
`node_modules` folder is needed. The JavaScript budget is **180.2 KB gzip** (gate: 250 KB).

The console runs under any prefix (`/agentprism`, `/panel`, …) and learns the prefix at
run time. Light and dark themes; the default follows the operating system.

**A slice of the HTTP surface:**

```csharp
// One entry point; access is restricted to loopback by default.
app.MapAgentPrism("/agentprism", options => options.RequireAuthorization("AgentPrismAdmin"));
```

```
GET    /agentprism/api/meta                    version · auth method · active stores  [anonymous]
GET    /agentprism/api/agents                  catalog (code + database)
POST   /agentprism/api/agents                  new definition    · PUT · DELETE · /versions · /rollback
POST   /agentprism/api/agents/{name}/run       streaming trial run over SSE
GET    /agentprism/api/sessions[/{id}]         sessions and conversation history · DELETE
GET    /agentprism/api/runs[/{id}]             run record
GET    /agentprism/api/runs/{id}/events        SSE; live or replay, resumable with Last-Event-ID
GET    /agentprism/api/tools · /api/models · /api/stats · /api/diagnostics
POST   /agentprism/api/attachments             upload an attachment · GET/DELETE

POST   /agentprism/v1/responses                OpenAI Responses API compatible
POST   /agentprism/v1/chat/completions         OpenAI Chat Completions API compatible
POST   /agentprism/v1/conversations            open a conversation · GET/DELETE · /items
```

With the stock OpenAI SDK:

```python
from openai import OpenAI

client = OpenAI(base_url="https://app.example.com/agentprism/v1", api_key="...")

# The 'model' field carries the agent name - no extra field is needed.
r = client.responses.create(model="support", input="Where is my order ORD-3")
print(r.output_text)          # Order ORD-3 has shipped. Estimated delivery: 2 days.

# Chaining a conversation: previous_response_id OR conversation - both work.
r2 = client.responses.create(model="support", input="And ORD-9?", previous_response_id=r.id)
```

The tool loop completes on the server, and every run is recorded event by event and can
be replayed over SSE.

**Defining an agent and its tools in code:**

```csharp
builder.AddAgentPrism()
       .AddToolsFrom(typeof(OrderTools))      // methods marked with [AgentPrismTool]
       .UseOpenAI(apiKey)
       .AddAgent(new AgentDefinition
       {
           Name = "support",
           Instructions = "You are a support assistant.",
           Model = new ModelBinding { Provider = OpenAIProviderNames.ChatCompletions, Model = "gpt-5.4-mini" },
           ToolNames = ["get_order_status", "cancel_order"],
       })
       .UsePostgreSql(connectionString)       // optional
       .UseMcp()                              // remote MCP tools, optional
       .UseUI();                              // embedded console

// Run with a durable session
var agent = await catalog.ResolveAsync("support");
var session = await sessions.GetOrCreateSessionAsync(agent!, "customer-42");
var response = await agent!.RunAsync("Where is my order?", session);
await sessions.SaveSessionAsync(agent, session);
```

```csharp
internal static class OrderTools
{
    [AgentPrismTool("get_order_status", "Returns the shipping status of an order.")]
    public static string GetOrderStatus(string orderId) => ...;

    public static string Helper() => "...";   // unmarked - not a tool
}
```

`UseOpenAI()` registers **two** providers: `openai` (Chat Completions) and
`openai-responses`; `ModelBinding.Provider` picks one. `UseOpenAICompatible(name, ...)`
connects the same package to **any** OpenAI-compatible endpoint — OpenRouter, Groq,
vLLM, a local Ollama or LM Studio (local servers need no `ApiKey`). Each provider is
checked for free with `GET {endpoint}/models`, and a circuit breaker stops a provider
that fails repeatedly.

Without `UsePostgreSql()`, storage falls back to memory and nothing breaks. The schema
is created by embedded SQL migrations in a separate `agentprism` schema; your
application's `public` schema is left alone.

A running example: [`samples/AgentPrism.Api`](samples/AgentPrism.Api).

---

## Why?

Microsoft Agent Framework reached GA with 1.16.0 and offers a capable agent runtime.
Its official developer interface, **DevUI**, is still preview, and its own
documentation says so plainly:

> "DevUI is a **sample app** to help you visualize and debug your agents and workflows
> during development. It is **not** intended for production use."

AgentPrism fills that gap. It does not replace DevUI — it continues where DevUI stops.

| | DevUI | AgentPrism |
|---|-------|------------|
| Purpose | Visualising during development | A control plane that runs in production |
| Persistence | In memory | PostgreSQL (separate `agentprism` schema) |
| Access | Loopback + static token | Loopback + token + authorization policy |
| Agent definitions | Read-only | Code + database, versioned, rollback-able |
| Multi-tenancy | None | `tenant_id` on every query |
| Audit trail | None | `audit_log` |
| .NET documentation | "Coming soon" | Published |

---

## Packages

| Package | What it does |
|---------|--------------|
| `AgentPrism` | Meta package — brings everything in with one reference |
| `AgentPrism.Abstractions` | Contracts; enough on its own if you write your own implementations |
| `AgentPrism.Core` | Runtime, catalog, definition compiler, tool registry, session management. **No database required.** |
| `AgentPrism.PostgreSql` | Persistence — embedded SQL migrations, separate `agentprism` schema |
| `AgentPrism.SqlServer` | SQL Server 2019+ and Azure SQL persistence — same schema, its own migration set. **Not in the meta package.** Contract tests run against a real `mssql/server` |
| `AgentPrism.Sqlite` | SQLite persistence — one file, table prefix, its own migration set. **Not in the meta package.** Single-writer; not for a multi-instance deployment |
| `AgentPrism.OpenAI` | OpenAI provider adapter — Chat Completions and Responses, tool calling, OpenTelemetry |
| `AgentPrism.Anthropic` | Anthropic (Claude) provider adapter — official SDK, prompt caching, extended thinking. **Not in the meta package** |
| `AgentPrism.Google` | Google Gemini provider adapter — official SDK, safety thresholds, thinking budget. **Not in the meta package**; brings a transitive `Google.Apis.Auth` chain |
| `AgentPrism.Azure` | Azure OpenAI provider adapter — deployment-based model resolution, API key or Entra identity. **Not in the meta package**; `Azure.Identity` is **not** a dependency, the credential factory comes from you |
| `AgentPrism.Voice` | Speech tools: `speak`, `transcribe`, `list_voices`, measured into `tool_invocations`. **Zero NuGet dependencies**; not in the meta package. Live conversation lives in `Core`: `UseVoiceConversation()` |
| `AgentPrism.Mcp` | Tool discovery from remote MCP servers — HTTP only, approval by default |
| `AgentPrism.Workflows` | Workflow execution — five patterns, checkpoints, resume, human-in-the-loop |
| `AgentPrism.AspNetCore` | HTTP layer — management API, OpenAI-compatible endpoints, multi-tenancy |
| `AgentPrism.UI` | Embedded React console — 28 screens across 36 routes, zero JavaScript dependencies |
| `AgentPrism.Templates` | The `dotnet new agentprism-api` template — not in the meta package |
| `AgentPrism.Testing` | `FakeModelProvider`, `AgentPrismTestHost`, `RunAssertions`; test-framework neutral, not in the meta package |

**Target frameworks:** `net8.0`, `net9.0`, `net10.0` · **License:** MIT

---

## Installation

```bash
dotnet add package AgentPrism --prerelease
```

### The model catalog

AgentPrism **ships no built-in model list**. Model names and prices change faster than
a NuGet package is released, and a list baked into code turns misleading quickly. The
catalog comes from configuration:

```json
{
  "AgentPrism": { "Providers": { "OpenAI": {
    "DefaultModel": "gpt-5.4-mini",
    "Models": [
      { "Name": "gpt-5.4-mini", "DisplayName": "GPT-5.4 mini",
        "ContextWindowTokens": 400000, "InputCostPerMillionTokens": 0.25 }
    ]
  }}}
}
```

The catalog is **not an allowlist**: a model name that is absent can still be used. The
list only feeds the console's model picker and the cost calculation.

### Set your secrets

A connection string and an API key **never** enter the repository. Use
`dotnet user-secrets`:

```bash
cd <your project>
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=...;Port=5432;Database=AgentPrism;Username=...;Password=..."

# or SQL Server, or SQLite (never all three at once; the last registration wins and a warning is logged)
dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" "Server=...,1433;Database=AgentPrism;User Id=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString"    "Data Source=agentprism.db"
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey"     "sk-..."
```

`appsettings.json` shows the shape only; it carries no value.

---

## Design rules

Four rules that do not change. Detail: [docs/MIMARI.md](docs/MIMARI.md) (Turkish).

**1. No surprises.** `AddAgentPrism()` works alone. Without PostgreSQL configured,
storage falls back to memory. A database is never required.

**2. Tools are defined in code only.** The console can create an agent; it can never
write tool **code**. It only lets you pick from the tools registered in code. This is a
security boundary.

**3. MAF objects are passed through, not wrapped.** `AIAgent`, `AgentSession`, and
`ChatMessage` are used directly. AgentPrism is a control plane, not an abstraction
layer.

**4. Every extension point is replaceable.** Every service is registered with
`TryAdd*`. Register your own implementation first and yours wins.

---

## Roadmap

Development runs in numbered phases. All are complete except the release phase, which
stays open because the release date is a deliberate decision. The list is generated from
each phase document into [docs/YOL-HARITASI.md](docs/YOL-HARITASI.md); unselected
candidates are in [docs/ADAYLAR.md](docs/ADAYLAR.md).

### Skill script execution and the isolation boundary

Skill scripts can run **on the server**. The feature is **off by default** and can only
be turned on in code:

```csharp
builder.Services.AddAgentPrism()
    .UseSkillScripts(options =>
    {
        options.PlatformIsolationAcknowledged = true;
        options.Interpreters["py"] = "python3";
    });
```

**AgentPrism provides no operating-system isolation.** A script runs with the user
rights and network access of the AgentPrism process. AgentPrism gives you an
interpreter and environment-variable allowlist, a timeout with process-tree kill,
output truncation, a concurrency limit, and per-tenant grants with an audit trail; it
gives you **no filesystem jail, no network restriction, no memory or CPU quota, and no
privilege dropping**. Those belong to the hosting environment: run inside a
**container**, as an **unprivileged user**, on a **restricted network**. The
`PlatformIsolationAcknowledged` flag stops the feature being enabled without seeing
this boundary; without it the application fails **at startup**.

### Content guards

**Off by default**: `AddAgentPrism()` registers no guard and adds no link to the model
pipeline. Turning it on is an explicit choice — `.AddPatternContentGuard(o =>
o.MaskedPii = PiiPatterns.CreditCard)` for the built-in pattern guard, or
`IContentGuard` with `.AddContentGuard<T>()` for your own rules. Several guards run in
order and **the strictest decision wins**; blocked content is written nowhere.

### Version policy

`Microsoft.Agents.AI.Hosting` (preview) and `Microsoft.Agents.AI.Hosting.OpenAI`
(alpha) are still pre-release. AgentPrism publishes as `1.0.0-preview.N` until both
reach GA.

The pre-release dependency lives only in `AgentPrism.AspNetCore`. Every other package
depends on GA packages only.

---

## Development

```bash
dotnet build  AgentPrism.slnx -c Release              # 0 warnings expected
dotnet test   AgentPrism.slnx -c Release --no-build   # 4408 tests, 16 projects
dotnet pack   AgentPrism.slnx -c Release --no-build
dotnet format AgentPrism.slnx --verify-no-changes
```

`TreatWarningsAsErrors` is on — there are no warnings, only errors.

Requirements: .NET SDK 10.0.100+, **Node.js 20.19+** (the console build), and **Docker**
(integration tests bring up a real database with Testcontainers). The console's
end-to-end tests download Chromium themselves on first run.

`dotnet build` builds the console too: `npm ci` → type check → Vitest → Vite → Brotli
compression → bundle budget gate. The steps are incremental and skipped when nothing
changed. For a fast inner loop, use `-p:AgentPrismFrontendEnabled=false`.

```bash
cd samples/AgentPrism.Api && dotnet run     # http://localhost:5080/agentprism

# Console only: the Vite dev server is faster (5173, proxying to 5080)
cd src/AgentPrism.UI/frontend && npm run dev
```

---

## Documentation

**The user-facing product documentation is a separate site:**
<https://farukatasoy.github.io/AgentPrism> — installation, your first agent, concepts, a
console tour, the HTTP API (160 operations), and an API reference for 671 public types.
Its source is [`docs-site/`](docs-site/), published on every push to `main`.

The table below is the **development documentation**: Turkish, and never mixed with the
site — `docs/` is the journal, `docs-site/` is the product documentation.

| Source | Contents |
|--------|----------|
| [docs/MIMARI.md](docs/MIMARI.md) · [MIMARI-GUVENLIK.md](docs/MIMARI-GUVENLIK.md) | Architecture — layers, data model, execution path · the security model |
| [docs/MAF-GENISLEME-NOKTALARI.md](docs/MAF-GENISLEME-NOKTALARI.md) | The MAF extension points we use, and the ones we deliberately do not |
| [docs/KARARLAR.md](docs/KARARLAR.md) · [index](docs/KARARLAR-INDEKS.md) · [rejected](docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md) | The decision ledger — lasting choices and rejected approaches, with their reasons |
| [docs/](docs/) `NN-*.md` · [arsiv/fazlar/](docs/arsiv/fazlar/) · [YOL-HARITASI.md](docs/YOL-HARITASI.md) · [ADAYLAR.md](docs/ADAYLAR.md) | Phase documents — open ones in `docs/`, closed ones (00–59) archived · phase status (generated) · unselected candidates |
| [docs/manuel-test/](docs/manuel-test/) | The manual acceptance-test specification; the `manuel-test-kosumu` skill drives a run |
| [docs/hafiza/](docs/hafiza/) · [docs/arsiv/](docs/arsiv/) | Area-specific traps · closed record (phase narrative, run rounds) |
| [AGENTS.md](AGENTS.md) · [MEMORY.md](MEMORY.md) · [.agents/skills/](.agents/skills/) | Agent instructions, memory routing, workflow skills (`CLAUDE.md` is a symlink to `AGENTS.md`) |
| [docs-site/](docs-site/) · [docfx/](docfx/) | **The product site** (English, Astro Starlight) and the API reference generator. A separate publishing pipeline; not attached to `dotnet build`. Needs Node 22.12+ |
| [scripts/dokuman-bakim.py](scripts/dokuman-bakim.py) | Generates the decision index and checks the documentation budgets |

---

## License

MIT — see [COMMERCIAL.md](COMMERCIAL.md) for the commercial-tier plan and the promise
that nothing which is MIT today will become paid.
