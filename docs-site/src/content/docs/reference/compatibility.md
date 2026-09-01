---
title: Compatibility
description: A verified compatibility matrix for target frameworks, packages, AOT, storage engines, model providers, UI ownership, and API-key scopes.
slug: reference/compatibility
---

Use this page before you choose a target framework, database, model provider, or
deployment shape. A check means the package makes that promise. A dash means the
capability does not apply; it does not mean “probably works.”

## Target frameworks

| Artifact | Target | Notes |
|---|---|---|
| Runtime packages | `net8.0`, `net9.0`, `net10.0` | This includes Core, all providers, all SQL packages, AspNetCore, MCP, Workflows, UI, and Voice |
| `AgentPrism.Testing` | `net10.0` | The test host follows the current MAF test surface |
| `AgentPrism.Templates` | Generates `net10.0` | It is a content package, not a runtime assembly |
| Embedded source generator | `netstandard2.0` | It ships through `AgentPrism.Core`; `AgentPrism.Generators` is not a separate public NuGet package |

The packages are still pre-release. Install them with an explicit preview version or
the CLI's pre-release option. The template package also needs a preview version or
pre-release selection.

## The 20 packages

“Meta” shows whether `dotnet add package AgentPrism` brings the package into the
dependency graph. `AOT` states the promise made by the package itself.

| Package | Meta | Frameworks | Native AOT | Purpose or limit |
|---|---:|---|---:|---|
| `AgentPrism` | — | net8/9/10 dependency groups | No | Meta package; it carries no assembly and includes packages that do not promise AOT |
| `AgentPrism.Abstractions` | Yes | net8/9/10 | Yes | Contracts and data types only |
| `AgentPrism.Core` | Yes | net8/9/10 | Yes | Catalog, compiler, decorators, in-memory stores, jobs, evaluation, and governance services |
| `AgentPrism.PostgreSql` | Yes | net8/9/10 | Yes | Durable stores and the only built-in vector-search store |
| `AgentPrism.OpenAI` | Yes | net8/9/10 | Yes | OpenAI Chat Completions, Responses, and named compatible endpoints |
| `AgentPrism.AspNetCore` | Yes | net8/9/10 | No | Minimal API delegate routing uses reflection |
| `AgentPrism.Workflows` | Yes | net8/9/10 | No | The MAF workflow engine uses reflection |
| `AgentPrism.Mcp` | Yes | net8/9/10 | No | Runtime MCP schemas and the MCP SDK use reflection for JSON handling |
| `AgentPrism.UI` | Yes | net8/9/10 | No | Embedded asset discovery plus its ASP.NET Core dependency |
| `AgentPrism.SqlServer` | No | net8/9/10 | No promise | Measured clean, but no live-query AOT guarantee is made |
| `AgentPrism.Sqlite` | No | net8/9/10 | No | `SQLitePCLRaw` carries a native library |
| `AgentPrism.Anthropic` | No | net8/9/10 | Yes | Claude provider |
| `AgentPrism.Google` | No | net8/9/10 | Yes | Gemini provider |
| `AgentPrism.Azure` | No | net8/9/10 | Yes | Azure OpenAI provider; managed identity stays consumer-selected |
| `AgentPrism.Voice` | No | net8/9/10 | Yes | ElevenLabs speech tools and reusable speech contracts |
| `AgentPrism.Testing` | No | net10 | No promise | Assertions use reflection and the test host uses runtime JSON serialization |
| `AgentPrism.Testing.Contracts.Xunit` | No | net8/9/10 | No promise | Behavior contract suite for five extension families — storage (`IRunStore` and 32 other store interfaces), model providers, run judges, agent sources, and custom tools — as xunit.v3 fixtures; uses reflection for a build-time coverage check |
| `AgentPrism.Templates` | No | net10 output | N/A | `dotnet new agentprism-api` content package |
| `AgentPrism.Client` | No | net8/9/10 | No | Typed management client generated from the OpenAPI document; every request/response call is hand-wired to a generic `JsonSerializer` overload the trim/AOT analyzer cannot prove type coverage for |
| `AgentPrism.Cli` | No | net10 (`DotnetTool`) | No | The `agentprism` global tool; wraps `AgentPrism.Client` and ships as IL, not native code |

`AgentPrism.Core` remains AOT-compatible because reflection-based convenience calls
carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. For AOT, prefer
`AddGeneratedTools()` or register an already-built `AIFunction`.

## Storage engines

All SQL providers implement the same store contracts and run the same shared contract
tests. The registration and provider-specific configuration still change.

| Capability | In memory | PostgreSQL | SQL Server | SQLite |
|---|---:|---:|---:|---:|
| Survives process restart | No | Yes | Yes | Yes |
| Intended for multiple application nodes | No | Yes | Yes | No; use it for one node or local work |
| Run, event, session, job, eval, workflow, audit, and governance stores | Process lifetime | Durable | Durable | Durable |
| Conversation branching | No | Yes | Yes | Yes |
| Vector knowledge search | No | Yes, with pgvector | No; knowledge answers `501` | No; knowledge answers `501` |
| Object isolation | Process | Schema, default `agentprism` | Schema, default `agentprism` | Table prefix, default `agentprism_` |
| Migration lock | None | PostgreSQL advisory lock | `sp_getapplock` | Sidecar file lock scoped to the database file and table prefix |
| Auto-apply migrations | N/A | On by default | On by default | On by default |
| Read contract view (`runs_v1`) | No | Yes, opt-in (`EnableReadViews`) | Yes, opt-in (`EnableReadViews`) | Yes, opt-in (`EnableReadViews`) |
| ORM | None | None | None | None |
| AOT promise | Yes through Core | Yes | No promise | No |

SQLite has no schema. Do not use bare `Data Source=:memory:` with AgentPrism. The
library opens more than one connection, so each bare in-memory connection would see a
different database. Use a shared URI such as
`Data Source=file:agentprism?mode=memory&cache=shared` when a SQLite in-memory test is
required.

PostgreSQL knowledge search also needs an
`IEmbeddingGenerator<string, Embedding<float>>`. The vector dimension becomes part of
the PostgreSQL column type. Changing the embedding dimension is a schema migration,
not a live configuration change.

## Persisted payload compatibility

| Payload | Owner | Compatible across a Microsoft Agent Framework version bump? |
|---|---|---|
| `SessionRecord` envelope (id, tenant, timestamps, `Version`, `StateSchemaVersion`, `StateMafVersion`) | AgentPrism | Yes — a minor AgentPrism version only adds envelope fields |
| `SessionRecord.State` | Microsoft Agent Framework | No promise |
| `WorkflowCheckpointRecord` envelope | AgentPrism | Yes |
| `WorkflowCheckpointRecord.State` | Microsoft Agent Framework | No promise |

See [Versions and upgrades](/reference/versioning/#persisted-session-and-checkpoint-state)
for what happens when a body cannot be read after an upgrade.

## Model providers

Built-in catalogs start empty. A catalog drives selection, health display, and cost
calculation; it never rejects an unlisted model or deployment.

| Provider package | Stable name | Default endpoint | Authentication | Compatibility notes |
|---|---|---|---|---|
| `AgentPrism.OpenAI` | `openai` | OpenAI | API key | Chat Completions surface |
| `AgentPrism.OpenAI` | `openai-responses` | OpenAI | API key | Responses surface; registered with the official provider |
| `AgentPrism.OpenAI` compatible registration | Caller-chosen name | Required or explicitly set by the caller | Optional for a local server | A second `{name}-responses` surface is opt-in because many compatible servers do not implement Responses |
| `AgentPrism.Anthropic` | `anthropic` | Anthropic | API key | `max_tokens` is mandatory upstream; AgentPrism defaults the output limit to 4,096 |
| `AgentPrism.Google` | `google` | Gemini Developer API | API key | Supports Google-specific safety and thinking settings in `ModelBinding.ProviderSettings` |
| `AgentPrism.Azure` | `azure-openai` | No global default; resource endpoint is required | API key or consumer `TokenCredential` factory | `ModelBinding.Model` contains an Azure deployment name, not the underlying model name |

The OpenAI-compatible path covers services such as OpenRouter and Groq and local
servers such as Ollama, LM Studio, and vLLM. Compatibility means the selected server
implements the surface you enable. It does not mean every server supports every
model feature.

## UI, API, configuration, and code ownership

The console is an operator surface. It does not turn deployment authority into a web
form. This matrix shows where a change can originate.

| Item | Console | Management API | Host code or configuration | Rule |
|---|---:|---:|---:|---|
| Executable tool implementation | View only | View only | Create and register | Tool code is code-only |
| Tool assignment to a database agent | Edit | Edit | Define | The tool must already be registered |
| Code-defined agent | View and run | View and run | Create and change | Code definitions are never edited in the console |
| Database-defined agent | Create, edit, diff, roll back | Create, edit, diff, roll back | Optional | The compiler validates before storage |
| Model-provider registration and credentials | View catalog and health | View catalog and health | Register and configure | Credentials never enter the database or UI |
| Model catalog and prices | View | Read | Configure | The catalog is informative, not an allowlist |
| Skills and stored skill content | Create and edit | Create and edit | Define in code or discover from configured roots | Script execution has additional deployment and grant gates |
| Skill-script grant | Manage in Skills | Manage | Configure the execution boundary | A grant cannot bypass disabled scripts or a missing interpreter |
| Workflow definition | Create, edit, inspect, run | Create, edit, inspect, run | Define in code | Code workflows are read-only in the console |
| Job schedule | Create, edit, trigger, delete | Create, edit, trigger, delete | Configure worker behavior | The job handler itself is code |
| Eval suite and experiment | Create and operate | Create and operate | Add custom checks and judges | Custom executable evaluation logic is code-only |
| Remote MCP server record | Manage | Manage | May seed or configure | Stored records refer to secret configuration keys, never secret values |
| Agent exposure as MCP or A2A | No | No | Explicit allowlist in `UseMcpServer()` or `UseA2A()` | External exposure is a deployment decision |
| Quota, API key, webhook, and retention policy | Settings panels | Manage | Supply defaults and secrets | Stored secrets are always references to configuration keys |
| Tenant | Show current tenant | Register and manage | Select resolver with `UseTenancy()` | There is no tenant-management screen |
| Authorization policy and role binding | Reflect effective access | No | ASP.NET Core configuration | AgentPrism stores no users or role assignments |
| Diagnostics endpoint | Use when present | Read when present | Enable in `MapAgentPrism()` options | Off by default |
| Theme and language | Change locally | N/A | N/A | Browser preference; server messages stay English |

## HTTP surface compatibility

The generated HTTP reference contains 143 management and OpenAI-compatible
operations grouped under 19 tags. It does not include every conditional route:

- `GET /api/diagnostics` exists only when `EnableDiagnosticsEndpoint` is true.
- The voice conversation route is a WebSocket and exists only after
  `UseVoiceConversation()`.
- MCP server and A2A routes use their own `MapAgentPrismMcpServer()` and
  `MapAgentPrismA2A()` calls.
- Health checks join the consumer's health-check system. You choose their route with
  ASP.NET Core `MapHealthChecks()`.
- UI assets and SPA fallback routes are not API operations.

The documented paths are relative to the prefix passed to `MapAgentPrism()`. With
the usual prefix, `GET /api/agents` means `GET /agentprism/api/agents`.

## API-key scopes

The JSON names are the PascalCase enum names below. Free-text scopes and names such
as `runs:write` are invalid. A scope narrows a role; it never widens one. Effective
authority is `role ∩ scopes`.

| Scope | Opens |
|---|---|
| `RunsRead` | Run summaries, event streams, statistics, traces, and other run reads |
| `RunsWrite` | Starting and cancelling runs and deciding approvals |
| `AgentsRead` | Agent, skill, tool, and model catalog reads |
| `AgentsAdmin` | Agent and skill definition writes and version rollback |
| `ExternalInvoke` | Explicitly exposed MCP-server and A2A invocation surfaces |
| `KnowledgeRead` | Knowledge collection listing and semantic search |
| `KnowledgeAdmin` | Knowledge document upload and deletion |
| `WorkflowsRead` | Workflow definitions, graphs, checkpoints, and pending requests |
| `WorkflowsAdmin` | Workflow definition writes and deletion; running still needs `RunsWrite` |
| `EvalsRead` | Eval suites, cases, runs, and online-evaluation summaries |
| `EvalsAdmin` | Eval suite and case writes; triggering a run still needs `RunsWrite` |
| `ExperimentsRead` | Experiments, results, and canary status |
| `ExperimentsAdmin` | Experiment writes, start and stop, and canary policy |
| `PlatformRead` | Tenant, quota, retention, scheduling, webhook, diagnostics, and provider-health reads |
| `PlatformAdmin` | Platform configuration writes and retention execution |
| `SecurityAdmin` | API keys, skill-script grants, and MCP OAuth start |
| `AuditRead` | Audit-trail reads |

`SecurityAdmin` can create or extend authority. Grant it rarely. API keys are stored
hashed and the plaintext value is returned only at creation.

## Read next

- [Versions and upgrades](/reference/versioning/) — what a version bump is allowed to change
- [Choosing packages](/packages/) — which of these matrices apply to you
- [Configuration](/reference/configuration/) — every option the supported combinations expose
