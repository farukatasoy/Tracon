---
title: Compatibility
description: A verified compatibility matrix for target frameworks, packages, AOT, storage engines, model providers, UI ownership, and API-key scopes.
slug: reference/compatibility
---

:::caution[Package availability]
Tracon packages and templates are not published yet. Package-install examples on
this page describe the release form and do not currently resolve from public
registries. With authorized repository access, use the
[source build instructions](/getting-started/first-agent/).
:::

Use this page before you choose a target framework, database, model provider, or
deployment shape. A check means the package makes that promise. A dash means the
capability does not apply; it does not mean “probably works.”

## Target frameworks

| Artifact | Target | Notes |
|---|---|---|
| Runtime packages | `net8.0`, `net9.0`, `net10.0` | This includes Core, all providers, all SQL packages, AspNetCore, MCP, Workflows, UI, and Voice |
| `Tracon.Testing` | `net8.0`, `net9.0`, `net10.0` | The test host resolves its ASP.NET Core hosting dependency per framework, so it matches the runtime matrix |
| `Tracon.Templates` | Generates `net10.0` | It is a content package, not a runtime assembly |
| Embedded source generator | `netstandard2.0` | It ships through `Tracon.Core`; `Tracon.Generators` is not a separate public NuGet package |

The packages are still pre-release. Install them with an explicit preview version or
the CLI's pre-release option. The template package also needs a preview version or
pre-release selection.

### How long each target framework stays

Tracon follows Microsoft's own .NET support lifecycle and does not extend it. A
target framework stays in the matrix while Microsoft still supports it; once
that support ends, a later Tracon release may drop it.

Plan for this before it happens, because it changes what you can upgrade to:

- **Dropping an out-of-support target framework is not treated as a breaking
  change** under the versioning policy. It can land in a minor release.
- A release that drops one says so in its changelog entry, and the package's
  own `net8.0`/`net9.0`/`net10.0` assets simply stop appearing.
- Your application keeps working on the release it already resolved; nothing
  is withdrawn from nuget.org. What ends is new Tracon releases for that
  framework.

Support dates come from Microsoft, not from Tracon, and they move: check the
[.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
for the current end-of-support date of each version before you pin one. `net10.0`
is the longest-lived target in the matrix today, and a new application should
start there.

## The 20 packages

“Meta” shows whether `dotnet add package Tracon` brings the package into the
dependency graph. `AOT` states the promise made by the package itself.

| Package | Meta | Frameworks | Native AOT | Purpose or limit |
|---|---:|---|---:|---|
| `Tracon` | — | net8/9/10 dependency groups | No | Meta package; it carries no assembly and includes packages that do not promise AOT |
| `Tracon.Abstractions` | Yes | net8/9/10 | Yes | Contracts and data types only |
| `Tracon.Core` | Yes | net8/9/10 | Yes | Catalog, compiler, decorators, in-memory stores, jobs, evaluation, and governance services |
| `Tracon.PostgreSql` | Yes | net8/9/10 | Yes | Durable stores and the only built-in vector-search store |
| `Tracon.OpenAI` | Yes | net8/9/10 | Yes | OpenAI Chat Completions, Responses, and named compatible endpoints |
| `Tracon.AspNetCore` | Yes | net8/9/10 | No | Minimal API delegate routing uses reflection |
| `Tracon.Workflows` | Yes | net8/9/10 | No | The MAF workflow engine uses reflection |
| `Tracon.Mcp` | Yes | net8/9/10 | No | Runtime MCP schemas and the MCP SDK use reflection for JSON handling |
| `Tracon.UI` | Yes | net8/9/10 | No | Embedded asset discovery plus its ASP.NET Core dependency |
| `Tracon.SqlServer` | No | net8/9/10 | No promise | Measured clean, but no live-query AOT guarantee is made |
| `Tracon.Sqlite` | No | net8/9/10 | No | `SQLitePCLRaw` carries a native library |
| `Tracon.Anthropic` | No | net8/9/10 | Yes | Claude provider |
| `Tracon.Google` | No | net8/9/10 | Yes | Gemini provider |
| `Tracon.Azure` | No | net8/9/10 | Yes | Azure OpenAI provider; managed identity stays consumer-selected |
| `Tracon.Voice` | No | net8/9/10 | Yes | ElevenLabs speech tools and reusable speech contracts |
| `Tracon.Testing` | No | net8/9/10 | No promise | Assertions use reflection and the test host uses runtime JSON serialization |
| `Tracon.Testing.Contracts.Xunit` | No | net8/9/10 | No promise | Behavior contract suite for six extension families — storage (`IRunStore` and 29 other store interfaces), model providers, run judges, agent sources, job handlers, and custom tools — as xunit.v3 fixtures; uses reflection for a build-time coverage check |
| `Tracon.Templates` | No | net10 output | N/A | `dotnet new tracon-api` content package |
| `Tracon.Client` | No | net8/9/10 | No | Typed management client generated from the OpenAPI document; every request/response call is hand-wired to a generic `JsonSerializer` overload the trim/AOT analyzer cannot prove type coverage for |
| `Tracon.Cli` | No | net10 (`DotnetTool`) | No | The `tracon` global tool; wraps `Tracon.Client` and ships as IL, not native code |

`Tracon.Core` remains AOT-compatible because reflection-based convenience calls
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
| Object isolation | Process | Schema, default `tracon` | Schema, default `tracon` | Table prefix, default `tracon_` |
| Migration lock | None | PostgreSQL advisory lock | `sp_getapplock` | Sidecar file lock scoped to the database file and table prefix |
| Auto-apply migrations | N/A | On by default | On by default | On by default |
| Read contract view (`runs_v1`) | No | Yes, opt-in (`EnableReadViews`) | Yes, opt-in (`EnableReadViews`) | Yes, opt-in (`EnableReadViews`) |
| ORM | None | None | None | None |
| AOT promise | Yes through Core | Yes | No promise | No |

SQLite has no schema. Do not use bare `Data Source=:memory:` with Tracon. The
library opens more than one connection, so each bare in-memory connection would see a
different database. Use a shared URI such as
`Data Source=file:tracon?mode=memory&cache=shared` when a SQLite in-memory test is
required.

PostgreSQL knowledge search also needs an
`IEmbeddingGenerator<string, Embedding<float>>`. The vector dimension becomes part of
the PostgreSQL column type. Changing the embedding dimension is a schema migration,
not a live configuration change.

## Agent Framework dependency versions

Seven packages depend on Microsoft Agent Framework or `Microsoft.Extensions.AI`.
These are the versions they declare.

| Package | Declared dependencies |
|---|---|
| `Tracon.Abstractions` | `Microsoft.Agents.AI.Abstractions` 1.20.0 · `Microsoft.Extensions.AI.Abstractions` 10.9.0 |
| `Tracon.Core` | `Microsoft.Agents.AI` 1.20.0 · `Microsoft.Agents.AI.Harness` 1.20.0 · `Microsoft.Extensions.AI` 10.9.0 · `Microsoft.Extensions.AI.Evaluation` 10.9.0 |
| `Tracon.Workflows` | `Microsoft.Agents.AI.Workflows` 1.20.0 |
| `Tracon.OpenAI` | `Microsoft.Agents.AI.OpenAI` 1.20.0 · `Microsoft.Extensions.AI.OpenAI` 10.9.0 |
| `Tracon.Azure` | `Microsoft.Extensions.AI.OpenAI` 10.9.0 |
| `Tracon.Testing.Contracts.Xunit` | `Microsoft.Agents.AI` 1.20.0 · `Microsoft.Extensions.AI` 10.9.0 |
| `Tracon.AspNetCore` | `Microsoft.Agents.AI.Hosting`, `.Hosting.AspNetCore`, and `.Hosting.A2A` at a 1.20.0 pre-release · `.Hosting.OpenAI` at a 1.20.0 alpha |

`Tracon.AspNetCore` is the only package that carries a pre-release Agent Framework
dependency, and it is the only one permitted to.

### A declared version is a floor, not a pin

Each entry above is a minimum. NuGet reads `1.20.0` as `[1.20.0, )` and then resolves
the **lowest** version that satisfies every constraint in the graph, so a project that
asks for nothing else restores exactly the version listed. You get a different version
only when something asks for one: a direct `PackageReference` to a higher version, or
another package whose own floor is higher.

Tracon declares **no upper bound**. A newer Agent Framework is allowed to resolve, and
Tracon will not stop it.

### What is tested, and what is promised

Tracon is built and tested against exactly the versions in the table. That is the
whole of the promise.

The promise is about **behaviour**, not about compiling. Agent Framework types appear
in only a few places on Tracon's own public surface — `AIAgent`, `AIFunction`, and
`ChatMessage` are the ones you are most likely to meet — so a newer Agent Framework
will often still compile. Underneath that surface Tracon calls into the framework
throughout its runtime. Compiling against an untested version is not evidence that it
behaves the same, and a resolution that silently moved is the case where you would
find out last.

To decide the version yourself instead of leaving it to the graph, reference the Agent
Framework packages explicitly at the version you intend, or commit a lock file.

### When Tracon moves the floor

A raised floor ships in a Tracon minor version and is named in the changelog. It can
oblige you to take a newer Agent Framework, so read it as you would any other
dependency bump. Tracon does not ship an upper bound to prevent one.

A version bump also reaches data you have already stored. The next section states
which stored payloads survive it.

## Persisted payload compatibility

| Payload | Owner | Compatible across a Microsoft Agent Framework version bump? |
|---|---|---|
| `SessionRecord` envelope (id, tenant, timestamps, `Version`, `StateSchemaVersion`, `StateMafVersion`) | Tracon | Yes — a minor Tracon version only adds envelope fields |
| `SessionRecord.State` | Microsoft Agent Framework | No promise |
| `WorkflowCheckpointRecord` envelope | Tracon | Yes |
| `WorkflowCheckpointRecord.State` | Microsoft Agent Framework | No promise |

See [Versions and upgrades](/reference/versioning/#persisted-session-and-checkpoint-state)
for what happens when a body cannot be read after an upgrade.

## Model providers

Built-in catalogs start empty. A catalog drives selection, health display, and cost
calculation; it never rejects an unlisted model or deployment.

| Provider package | Stable name | Default endpoint | Authentication | Compatibility notes |
|---|---|---|---|---|
| `Tracon.OpenAI` | `openai` | OpenAI | API key | Chat Completions surface |
| `Tracon.OpenAI` | `openai-responses` | OpenAI | API key | Responses surface; registered with the official provider |
| `Tracon.OpenAI` compatible registration | Caller-chosen name | Required or explicitly set by the caller | Optional for a local server | A second `{name}-responses` surface is opt-in because many compatible servers do not implement Responses |
| `Tracon.Anthropic` | `anthropic` | Anthropic | API key | `max_tokens` is mandatory upstream; Tracon defaults the output limit to 4,096 |
| `Tracon.Google` | `google` | Gemini Developer API | API key | Supports Google-specific safety and thinking settings in `ModelBinding.ProviderSettings` |
| `Tracon.Azure` | `azure-openai` | No global default; resource endpoint is required | API key or consumer `TokenCredential` factory | `ModelBinding.Model` contains an Azure deployment name, not the underlying model name |

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
| Authorization policy and role binding | Reflect effective access | No | ASP.NET Core configuration | Tracon stores no users or role assignments |
| Diagnostics endpoint | Use when present | Read when present | Enable in `MapTracon()` options | Off by default |
| Theme and language | Change locally | N/A | N/A | Browser preference; server messages stay English |

## The production profile is a versioned contract

`RequireProductionProfile()` asks about a fixed set of production decisions. That
set is part of the compatibility contract, and it is the one place where a minor
release can deliberately stop a host that used to start.

| Question | Answer |
|---|---|
| Can a release add a decision to the profile? | Yes. It is the only way a newly recognised risk reaches a deployment that asked to be told |
| What happens to a host that calls the method? | It does not start until the new decision is answered or the risk is accepted by name |
| How is that announced? | As a **behavioural breaking change** in `CHANGELOG.md`, naming the new risk and saying why it was added — "hardened" on its own is not enough |
| What happens to a host that does not call the method? | Nothing. Every default stays exactly where it was; the profile changes no setting |
| Does accepting a risk survive the upgrade? | Yes. An accept names one risk and keeps applying to that risk alone |

Read that cost before adopting the method. A deployment that wants its production
decisions pinned rather than re-asked should pin the package version, not avoid the
gate: the alternative is a new decision arriving in silence, which is the thing the
method exists to prevent.

## HTTP surface compatibility

The generated HTTP reference contains 168 management and OpenAI-compatible
operations grouped under 23 domain tags.

Being in the reference and being live in your process are different questions.
The document is generated from one build profile; whether a route answers depends
on how you called `MapTracon()`:

| Route | In the reference | Live in your process |
|---|---|---|
| `GET /api/diagnostics` | Yes | Only when `EnableDiagnosticsEndpoint` is true |
| Voice conversation | No — a WebSocket, not an operation | Only after `UseVoiceConversation()` |
| MCP server routes | No | Only after `MapTraconMcpServer()` |
| A2A routes | No | Only after `MapTraconA2A()` |
| Health checks | No — they join your own health-check system | Wherever you put `MapHealthChecks()` |
| UI assets and SPA fallback | No — not API operations | Only after `UseUI()` |

So a route present in the reference is not automatically enabled for you, and a
route absent from it is not automatically unavailable.

The documented paths are relative to the prefix passed to `MapTracon()`. With
the usual prefix, `GET /api/agents` means `GET /tracon/api/agents`.

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
