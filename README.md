# Tracon

**A production-grade agent control plane for the Microsoft Agent Framework.**

Tracon is a .NET package family built on
[Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/).
You write the AI harness; you operate it at `/tracon`.

> **Status:** in development, **not yet published** — nothing is on NuGet or npm and
> there is no release tag, so the install command below does not resolve yet. Build
> from this repository. Tracon is **operable**, its
> [product documentation is published](https://tracon.dev), and the
> **public API gate** (`EnablePublicApiTracking`) is on independently of any release
> decision — an unrecorded surface change breaks the build. Start with
> `dotnet new tracon-api` and test without calling a model using
> `Tracon.Testing`.
>
> - Runs recorded with spans, metrics and cost; tenants isolated; audit trail made
>   tamper-evident with a hash chain (`GET /api/audit/verify`)
> - Agents exposed over MCP and A2A; `pgvector` semantic search; API keys narrow access
> - A tenant can bring its own model provider key (BYOK) under an egress policy —
>   an unauthorised provider is rejected at compile time
> - A/B experiments roll back automatically under a canary rule; a data subject's
>   content can be exported and erased by identity (`IDataSubjectResolver`)
> - An external system such as Slack can start a queued run with one signed HTTP
>   request and no API key; a tool body can run in the browser (`AddClientTool`)
>
> The **coding agent** integrating the package learns from build-time diagnostics and
> an opt-in `AGENTS.md` capability map.

```csharp
builder.AddTracon()
       .UsePostgreSql(connectionString)
       .UseOpenAI(apiKey)
       .AddTool(GetOrderStatus)
       .UseUI();

app.MapTracon("/tracon");
```

Two lines: a working agent, durable sessions, and a control plane at
`http://localhost:5080/tracon`.

### The console

Dashboard, Agents, Skills, Playground, Sessions, Runs, Workflows, Jobs, Evals,
Experiments, Approvals, Tools, Models, MCP, Triggers, Audit, Diagnostics, Settings —
**30 screens across 36 routes**, lists and editors included.

Written in React 19 and TypeScript, built with Vite, and embedded in the assembly
**Brotli-compressed**. No JavaScript dependency appears in the consuming project and no
`node_modules` folder is needed. The JavaScript budget is **184.1 KB gzip** (gate: 250 KB),
and a gate fails the build on a fifth run-time dependency.

The console runs under any prefix (`/tracon`, `/panel`, …) and learns the prefix at
run time. Navigation is split into **Operate** and **Configure**. A dark instrument
palette is the default; a light theme and "follow system" are both offered.

**A slice of the HTTP surface:**

```csharp
// One entry point; access is restricted to loopback by default.
app.MapTracon("/tracon", options => options.RequireAuthorization("TraconAdmin"));
```

```
GET    /tracon/api/meta                    version · auth method · active stores  [anonymous]
GET    /tracon/api/agents                  catalog (code + database)
POST   /tracon/api/agents                  new definition    · PUT · DELETE · /versions · /rollback
POST   /tracon/api/agents/{name}/run       streaming trial run over SSE
GET    /tracon/api/sessions[/{id}]         sessions and conversation history · DELETE
GET    /tracon/api/runs[/{id}]             run record
GET    /tracon/api/runs/{id}/events        SSE; live or replay, resumable with Last-Event-ID
GET    /tracon/api/tools · /api/models · /api/stats · /api/diagnostics
POST   /tracon/api/attachments             upload an attachment · GET/DELETE

POST   /tracon/v1/responses                OpenAI Responses API compatible
POST   /tracon/v1/chat/completions         OpenAI Chat Completions API compatible
POST   /tracon/v1/conversations            open a conversation · GET/DELETE · /items
```

With the stock OpenAI SDK:

```python
from openai import OpenAI

client = OpenAI(base_url="https://app.example.com/tracon/v1", api_key="...")

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
builder.AddTracon()
       .AddToolsFrom(typeof(OrderTools))      // methods marked with [TraconTool]
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
    [TraconTool("get_order_status", "Returns the shipping status of an order.")]
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
is created by embedded SQL migrations in a separate `tracon` schema; your
application's `public` schema is left alone.

A running example: [`samples/Tracon.Api`](samples/Tracon.Api).

---

## Why?

Microsoft Agent Framework reached GA with 1.16.0 and offers a capable agent runtime.
Its official developer interface, **DevUI**, is still preview, and its own
documentation says so plainly:

> "DevUI is a **sample app** to help you visualize and debug your agents and workflows
> during development. It is **not** intended for production use."

Tracon fills that gap. It does not replace DevUI — it continues where DevUI stops.

| | DevUI | Tracon |
|---|-------|------------|
| Purpose | Visualising during development | A control plane that runs in production |
| Persistence | In memory | PostgreSQL (separate `tracon` schema) |
| Access | Loopback + static token | Loopback + token + authorization policy |
| Agent definitions | Read-only | Code + database, versioned, rollback-able |
| Multi-tenancy | None | `tenant_id` on every query |
| Audit trail | None | `audit_log` |
| .NET documentation | "Coming soon" | Published |

---

## Packages

| Package | Licence | What it does |
|---------|---------|--------------|
| `Tracon` | PolyForm | Meta package — brings everything in with one reference |
| `Tracon.Abstractions` | MIT | Contracts; enough on its own if you write your own implementations |
| `Tracon.Core` | PolyForm | Runtime, catalog, definition compiler, tool registry, session management. **No database required.** |
| `Tracon.PostgreSql` | PolyForm | Persistence — embedded SQL migrations, separate `tracon` schema |
| `Tracon.SqlServer` | PolyForm | SQL Server 2019+ and Azure SQL persistence — same schema, its own migration set. **Not in the meta package.** Contract tests run against a real `mssql/server` |
| `Tracon.Sqlite` | PolyForm | SQLite persistence — one file, table prefix, its own migration set. **Not in the meta package.** Single-writer; not for a multi-instance deployment |
| `Tracon.OpenAI` | PolyForm | OpenAI provider adapter — Chat Completions and Responses, tool calling, OpenTelemetry |
| `Tracon.Anthropic` | PolyForm | Anthropic (Claude) provider adapter — official SDK, prompt caching, extended thinking. **Not in the meta package** |
| `Tracon.Google` | PolyForm | Google Gemini provider adapter — official SDK, safety thresholds, thinking budget. **Not in the meta package**; brings a transitive `Google.Apis.Auth` chain |
| `Tracon.Azure` | PolyForm | Azure OpenAI provider adapter — deployment-based model resolution, API key or Entra identity. **Not in the meta package**; `Azure.Identity` is **not** a dependency, the credential factory comes from you |
| `Tracon.Voice` | PolyForm | Speech tools: `speak`, `transcribe`, `list_voices`, measured into `tool_invocations`. **Zero NuGet dependencies**; not in the meta package. Live conversation lives in `Core`: `UseVoiceConversation()` |
| `Tracon.Mcp` | PolyForm | Tool discovery from remote MCP servers — HTTP only, approval by default |
| `Tracon.Workflows` | PolyForm | Workflow execution — five patterns, checkpoints, resume, human-in-the-loop |
| `Tracon.AspNetCore` | PolyForm | HTTP layer — management API, OpenAI-compatible endpoints, multi-tenancy |
| `Tracon.UI` | PolyForm | Embedded React console — 30 screens across 36 routes, zero JavaScript dependencies |
| `Tracon.Templates` | MIT | The `dotnet new tracon-api` template — not in the meta package |
| `Tracon.Testing` | PolyForm | `FakeModelProvider`, `TraconTestHost`, `RunAssertions`; test-framework neutral, not in the meta package |
| `Tracon.Testing.Contracts.Xunit` | MIT | The behavior-contract suites the shipped implementations run — derive from them to verify your own `IRunStore`, `IModelProvider`, `IRunJudge`, `IAgentSource`, `IJobHandler`, or custom tool. Not in the meta package |
| `Tracon.Client` | PolyForm | Typed management client generated from the OpenAPI document — 162 operations, zero Tracon dependency, zero NuGet dependency beyond DI abstractions. Not in the meta package |
| `Tracon.Cli` | PolyForm | The `tracon` global tool (`dotnet tool install -g Tracon.Cli`) — `migrate`, `migrate status`, `health`. Not a library; not in the meta package |
| [`@tracon/client`](https://www.npmjs.com/package/@tracon/client) | PolyForm | **npm, not NuGet** — the same 165 operations as `Tracon.Client`, generated from the same OpenAPI document with `openapi-typescript` + `openapi-fetch`. `npm install @tracon/client` |

**Target frameworks:** `net8.0`, `net9.0`, `net10.0`

**Licence:** PolyForm Small Business 1.0.0 — free for an individual, an open source
project, and any company with fewer than 100 people and under 1,000,000 USD (2019,
inflation adjusted) revenue in the prior tax year. Above that, a commercial licence
applies. Three packages are MIT instead, so that writing and testing an extension,
and owning what `dotnet new` generates, never needs one. Full terms: [LICENSE.md](LICENSE.md)
and [LICENSE-MIT.md](LICENSE-MIT.md); the reasoning behind the split is on the
[licensing page](https://tracon.dev/reference/licensing/).

### AOT compatibility

Eight packages promise trimming and Native AOT compatibility:
`Tracon.Abstractions`, `Core`, `PostgreSql`, `OpenAI`, `Anthropic`,
`Google`, `Azure`, and `Voice`. The other twelve do not — including the
**meta package `Tracon` itself**, since it pulls in `Tracon.AspNetCore`
and `Tracon.UI`, neither of which makes the promise. Reference the meta
package expecting "the whole family is AOT-safe" and `PublishAot` fails on
one of those two. Per-package reasons:
[Compatibility reference](https://tracon.dev/reference/compatibility/).

---

## Installation

**Not published yet** — this is what installation will look like. Until the first
release, reference the projects from a clone.

```bash
dotnet add package Tracon --prerelease
```

### The model catalog

Tracon **ships no built-in model list**. Model names and prices change faster than
a NuGet package is released, and a list baked into code turns misleading quickly. The
catalog comes from configuration:

```json
{
  "Tracon": { "Providers": { "OpenAI": {
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
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" "Host=...;Port=5432;Database=Tracon;Username=...;Password=..."

# or SQL Server, or SQLite (never all three at once; the last registration wins and a warning is logged)
dotnet user-secrets set "Tracon:SqlServer:ConnectionString" "Server=...,1433;Database=Tracon;User Id=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "Tracon:Sqlite:ConnectionString"    "Data Source=tracon.db"
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey"     "sk-..."
```

`appsettings.json` shows the shape only; it carries no value.

---

## Design rules

Four rules that do not change. Detail: [docs/MIMARI.md](docs/MIMARI.md) (Turkish).

**1. No surprises.** `AddTracon()` works alone. Without PostgreSQL configured,
storage falls back to memory. A database is never required.

**2. Tools are defined in code only.** The console can create an agent; it can never
write tool **code**. It only lets you pick from the tools registered in code. This is a
security boundary.

**3. MAF objects are passed through, not wrapped.** `AIAgent`, `AgentSession`, and
`ChatMessage` are used directly. Tracon is a control plane, not an abstraction
layer.

**4. Every extension point is replaceable.** Every service is registered with
`TryAdd*`. Register your own implementation first and yours wins.

---

## Roadmap

Development runs in numbered phases. All are complete except the release phase, which
stays open because the release date is a deliberate decision. The list is generated from
each phase document into [docs/YOL-HARITASI.md](docs/YOL-HARITASI.md); unselected
candidates are in [docs/ADAYLAR.md](docs/ADAYLAR.md).

### Skill script execution and content guards

Both are **off by default** and turned on explicitly in code
(`.UseSkillScripts(...)`, `.AddPatternContentGuard(...)`/`.AddContentGuard<T>()`).
Skill scripts run on the server with no OS-level isolation from Tracon itself —
that boundary is the hosting environment's job (container, unprivileged user,
restricted network). Full behavior, the security boundary, and the guard decision
model: [docs/MIMARI-GUVENLIK.md](docs/MIMARI-GUVENLIK.md) (Turkish) ·
[product docs](https://tracon.dev).

### Version policy

`Microsoft.Agents.AI.Hosting` (preview) and `Microsoft.Agents.AI.Hosting.OpenAI`
(alpha) are still pre-release. Tracon publishes as `1.0.0-preview.N` until both
reach GA.

The pre-release dependency lives only in `Tracon.AspNetCore`. Every other package
depends on GA packages only.

---

## Development

```bash
dotnet build  Tracon.slnx -c Release              # 0 warnings expected
dotnet test   Tracon.slnx -c Release --no-build   # 20 test projects
dotnet pack   Tracon.slnx -c Release --no-build
dotnet format Tracon.slnx --verify-no-changes
```

`TreatWarningsAsErrors` is on — there are no warnings, only errors.

Requirements: .NET SDK 10.0.100+, **Node.js 20.19+** (the console build), and **Docker**
(integration tests bring up a real database with Testcontainers). The console's
end-to-end tests download Chromium themselves on first run.

`dotnet build` builds the console too: `npm ci` → type check → Vitest → Vite → Brotli
compression → bundle budget gate. The steps are incremental and skipped when nothing
changed. For a fast inner loop, use `-p:TraconFrontendEnabled=false`.

```bash
cd samples/Tracon.Api && dotnet run     # http://localhost:5080/tracon

# Console only: the Vite dev server is faster (5173, proxying to 5080)
cd src/Tracon.UI/frontend && npm run dev
```

---

## Documentation

**The user-facing product documentation is a separate site:**
<https://tracon.dev> — installation, your first agent, concepts, a
console tour, the HTTP API (165 operations), and an API reference for 671 public types.
Its source is [`docs-site/`](docs-site/); [`scripts/site-deploy.sh`](scripts/site-deploy.sh)
builds it, runs the four site gates, and publishes it. The serving stack is in
[`docs-site/deploy/`](docs-site/deploy/) — an nginx container behind Traefik — so the
host is never configured by hand.

The table below is the **development documentation**: Turkish, and never mixed with the
site — `docs/` is the journal, `docs-site/` is the product documentation.

| Source | Contents |
|--------|----------|
| [docs/MIMARI.md](docs/MIMARI.md) · [MIMARI-GUVENLIK.md](docs/MIMARI-GUVENLIK.md) | Architecture — layers, data model, execution path · the security model |
| [docs/MAF-GENISLEME-NOKTALARI.md](docs/MAF-GENISLEME-NOKTALARI.md) | The MAF extension points we use, and the ones we deliberately do not |
| [docs/KARARLAR.md](docs/KARARLAR.md) · [index](docs/KARARLAR-INDEKS.md) · [rejected](docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md) | The decision ledger — lasting choices and rejected approaches, with their reasons |
| [docs/](docs/) `NN-*.md` · [arsiv/fazlar/](docs/arsiv/fazlar/) · [YOL-HARITASI.md](docs/YOL-HARITASI.md) · [ADAYLAR.md](docs/ADAYLAR.md) | Phase documents — open ones in `docs/`, closed ones (00–89) archived · phase status (generated) · unselected candidates |
| [docs/manuel-test/](docs/manuel-test/) | The manual acceptance-test specification; the `manuel-test-kosumu` skill drives a run |
| [docs/hafiza/](docs/hafiza/) · [docs/arsiv/](docs/arsiv/) | Area-specific traps · closed record (phase narrative, run rounds) |
| [CONTRIBUTING.md](CONTRIBUTING.md) · [ARCHITECTURE.md](ARCHITECTURE.md) | **English** — how to build, which gate to run, which test level a change needs · a short architecture map for people changing the code |
| [AGENTS.md](AGENTS.md) · [MEMORY.md](MEMORY.md) · [.agents/skills/](.agents/skills/) | Agent instructions, memory routing, workflow skills (`CLAUDE.md` is a symlink to `AGENTS.md`) |
| [docs-site/](docs-site/) · [docfx/](docfx/) | **The product site** (English, Astro Starlight) and the API reference generator. A separate publishing pipeline; not attached to `dotnet build`. Needs Node 22.12+ |
| [scripts/dokuman-bakim.py](scripts/dokuman-bakim.py) | Generates the decision index and checks the documentation budgets |

---

## Licence

**PolyForm Small Business 1.0.0**, except for `Tracon.Abstractions`,
`Tracon.Testing.Contracts.Xunit` and `Tracon.Templates`, which are MIT.

Use is free of charge if your company has fewer than 100 total individuals working
as employees and independent contractors, and less than 1,000,000 USD (2019, adjusted
for inflation) total revenue in the prior tax year. An individual, a student and an
open source project are all under that threshold. Above it, write to
hfarukatasoy@gmail.com for a commercial licence.

No package contains a licence key, an activation call or a feature gate, and the
licence of a version already published never changes.

Terms: [LICENSE.md](LICENSE.md) · [LICENSE-MIT.md](LICENSE-MIT.md) · which package is
which, and why: <https://tracon.dev/reference/licensing/>
