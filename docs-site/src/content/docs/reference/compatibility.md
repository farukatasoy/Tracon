---
title: Compatibility
description: A verified compatibility matrix for target frameworks, packages, AOT, storage engines, model providers, UI ownership, and API-key scopes.
slug: reference/compatibility
---

:::note[Preview packages]
Tracon is published as `1.0.0-preview.6`. Use `--prerelease` for discovery or
pin the exact version for reproducible builds.
:::

Use this page before you choose a target framework, database, model provider, or
deployment shape. A check means the package makes that promise. A dash means the
capability does not apply; it does not mean “probably works.”

## Target frameworks

| Artifact | Target | Notes |
|---|---|---|
| Runtime packages | `net10.0` | This includes Core, all providers, all SQL packages, AspNetCore, MCP, Workflows, UI, Voice, and AG-UI |
| `Tracon.Testing` | `net10.0` | It follows the runtime packages, so an app is tested on the framework it runs on |
| `Tracon.Templates` | Generates `net10.0` | It is a content package, not a runtime assembly |
| Embedded source generator | `netstandard2.0` | It ships through `Tracon.Core`; `Tracon.Generators` is not a separate public NuGet package |

Every test suite in Tracon's own CI runs on `net10.0`, the one target the runtime
packages build for.

The packages are still pre-release. Install them with an explicit preview version or
the CLI's pre-release option. The template package also needs a preview version or
pre-release selection.

### C# language version

A project that references Tracon compiles with `LangVersion` 9 or later. The public API
uses init-only setters, which C# 8 does not accept, and the files the source generator
writes into your project use no syntax newer than C# 9. The default for `net10.0` is far
above this floor, so the sentence matters only to a project that pins `LangVersion`
lower. A strict build (warnings as errors, every analyzer on) with `LangVersion` pinned
to 9, 10, and 11 runs against the packed packages in Tracon's own CI.

### How long each target framework stays

Tracon follows Microsoft's own .NET support lifecycle and does not extend it. A
target framework normally stays in the matrix while Microsoft still supports it;
once that support ends, a later Tracon release may drop it. One drop was not
tied to that date: `net8.0` and `net9.0`, described below.

Plan for this before it happens, because it changes what you can upgrade to:

- **Dropping an out-of-support target framework is not treated as a breaking
  change** under the versioning policy. It can land in a minor release.
- A release that drops one says so in its changelog entry, and the package's
  own assets for that framework simply stop appearing.
- Your application keeps working on the release it already resolved; nothing
  is withdrawn from nuget.org. What ends is new Tracon releases for that
  framework.

Support dates come from Microsoft, not from Tracon, and they move: check the
[.NET support policy](https://dotnet.microsoft.com/platform/support/policy/dotnet-core)
for the current end-of-support date of each version before you pin one. `net10.0`
is the only target in the matrix today, and a new application should start there.

**Dropped after `1.0.0-preview.6`:** `net8.0` and `net9.0` reach the end of
Microsoft support on 2026-11-10. The announcement of 2026-09-23 said the first
Tracon release after that date would drop both targets. The drop is no longer
tied to that date: the first release after `1.0.0-preview.6` drops them,
whether it ships before or after 2026-11-10. `1.0.0-preview.6` is the last release
that carries them, and it stays on nuget.org. An application that
targets `net8.0` or `net9.0` cannot restore a newer release (`NU1202`); move it
to `net10.0` first.

## The 21 packages

“Meta” shows whether `dotnet add package Tracon` brings the package into the
dependency graph. `AOT` states the promise made by the package itself.

| Package | Meta | Frameworks | Native AOT | Purpose or limit |
|---|---:|---|---:|---|
| `Tracon` | — | net10 dependency group | No | Meta package; it carries no assembly and includes packages that do not promise AOT |
| `Tracon.Abstractions` | Yes | net10 | Yes | Contracts and data types only |
| `Tracon.Core` | Yes | net10 | Yes | Catalog, compiler, decorators, in-memory stores, jobs, evaluation, and governance services |
| `Tracon.PostgreSql` | No | net10 | Yes | Durable stores and the only built-in vector-search store |
| `Tracon.OpenAI` | Yes | net10 | Yes | OpenAI Chat Completions, Responses, and named compatible endpoints |
| `Tracon.AspNetCore` | Yes | net10 | No | Minimal API delegate routing uses reflection |
| `Tracon.Workflows` | Yes | net10 | No | The MAF workflow engine uses reflection |
| `Tracon.Mcp` | Yes | net10 | No | Runtime MCP schemas and the MCP SDK use reflection for JSON handling |
| `Tracon.UI` | Yes | net10 | No | Embedded asset discovery plus its ASP.NET Core dependency |
| `Tracon.SqlServer` | No | net10 | No promise | Measured clean, but no live-query AOT guarantee is made |
| `Tracon.Sqlite` | No | net10 | No | `SQLitePCLRaw` carries a native library |
| `Tracon.Anthropic` | No | net10 | No | Claude provider; the Anthropic SDK's `IChatClient` adapter uses reflection-based JSON, so a Native AOT app fails on the first request |
| `Tracon.Google` | No | net10 | No | Gemini provider; its credential library (`Google.Apis.Auth`) uses reflection-based JSON and a Native AOT publish emits about 100 trim and AOT warnings |
| `Tracon.Azure` | No | net10 | Yes | Azure OpenAI provider (v1 API); managed identity stays consumer-selected |
| `Tracon.Voice` | No | net10 | Yes | ElevenLabs speech tools and reusable speech contracts |
| `Tracon.AGUI` | No | net10 | No | The AG-UI endpoint; it sits on `Tracon.AspNetCore`, whose minimal API routing uses reflection |
| `Tracon.Testing` | No | net10 | No promise | Assertions use reflection and the test host uses runtime JSON serialization |
| `Tracon.Testing.Contracts.Xunit` | No | net10 | No promise | Behavior contract suite for seven extension families — storage (`IRunStore` and 32 other store interfaces), model providers, run judges, agent sources, job handlers, skill script executors, and custom tools — as xunit.v3 fixtures; uses reflection for a build-time coverage check |
| `Tracon.Templates` | No | net10 output | N/A | `dotnet new tracon-api` content package |
| `Tracon.Client` | No | net10 | No | Typed management client generated from the OpenAPI document; every request/response call is hand-wired to a generic `JsonSerializer` overload the trim/AOT analyzer cannot prove type coverage for |
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
| Intended for multiple application nodes | No | Yes; two instances are supported, see [Run more than one instance](/guides/multiple-instances/) | Yes; two instances are supported, see [Run more than one instance](/guides/multiple-instances/) | No; use it for one node or local work |
| Run, event, session, job, eval, workflow, audit, and governance stores | Process lifetime | Durable | Durable | Durable |
| Conversation branching | No | Yes | Yes | Yes |
| Vector knowledge search | No | Yes, with pgvector | No; knowledge answers `501` | No; knowledge answers `501` |
| Object isolation | Process | Schema, default `tracon` | Schema, default `tracon` | Table prefix, default `tracon_` |
| Migration lock | None | PostgreSQL advisory lock | `sp_getapplock` | Sidecar file lock scoped to the database file and table prefix |
| Auto-apply migrations | N/A | On by default | On by default | On by default |
| Read contract view (`runs_v1`) | No | Yes, opt-in (`EnableReadViews`) | Yes, opt-in (`EnableReadViews`) | Yes, opt-in (`EnableReadViews`) |
| Identifiers and names a client chooses (session id, idempotency key, file path, checkpoint id, score author, the name of an agent, workflow, skill or other definition, the name of an experiment variant, and the identity of a user: a session owner, a run user, an audit actor) | Exact | Exact | Compared by code point, so letter case counts: those columns use `Latin1_General_BIN2` whatever the collation of the database. SQL Server ignores trailing spaces in any collation, so `abc` and `abc ` are one key there; the run, `/v1/responses`, AG-UI and session-branch endpoints refuse a session id that starts or ends with whitespace (`400`) so the stores agree on the ids that open a session | Exact |
| Tenant identifier | ASCII letters, digits, `_`, `.` and `-` only, folded to lower case; every store refuses any other character with an `ArgumentException` | Same rule | Same rule. The tenant columns keep the collation of the database, which ignores accents and width: no two different identifiers over that alphabet compare equal there, and that is why the alphabet is closed | Same rule |
| ORM | None | None | None | None |
| AOT promise | Yes through Core | Yes | No promise | No |

SQLite has no schema. Do not use bare `Data Source=:memory:` with Tracon. The
library opens more than one connection, so each bare in-memory connection would see a
different database. Use a shared URI such as
`Data Source=file:tracon?mode=memory&cache=shared` when a SQLite in-memory test is
required.

On SQL Server the names of definitions (agents, workflows, skills, schedules and similar), and every column that repeats one
(the agent of a run or a session, the tool of an invocation, the entity of an audit entry), use `Latin1_General_BIN2` too: `Support` and `support` are two agents on
every store, and a request that spells a name differently from the definition answers `404`. A query of your own that joins one of these
columns to a column of another collation fails with "Cannot resolve the collation conflict"; write `COLLATE Latin1_General_BIN2` on your side.
SQL Server ignores trailing spaces in any collation, so `Support` and `Support ` are one name there.

PostgreSQL knowledge search also needs an
`IEmbeddingGenerator<string, Embedding<float>>`. The vector dimension becomes part of
the PostgreSQL column type. Changing the embedding dimension is a schema migration,
not a live configuration change.

## Verified platforms

This section lists what was run, not what is expected to work. A cell is **verified** when the test project named in its
row ran to the end on it with no test failing; **not verified** means no run exists, however likely it is to work. A version
below the lowest verified one has no run behind it, and a cell with an exception says what the exception is.

Every row names the test project that proves it and the job of the CI workflow that runs it. The jobs `postgres-matrix`,
`sqlserver-matrix`, `sqlite-floor` and `culture-matrix` were written after the runs below and have **not run on GitHub
yet**; until they do, each row rests on one run of the same command on a development machine (Apple silicon, 10 cores, the
database servers as Linux containers), dated in the row. The test counts are those of the commit the page describes.

### PostgreSQL

`Tracon.PostgreSql.IntegrationTests` applies every migration and runs every store contract, 1 594 tests, one of which is
skipped by design. The test image is `pgvector/pgvector`; the knowledge store needs the `vector` extension and every run
turns it on.

| Server | Result | Run |
|---|---|---|
| 12.19, pgvector 0.7.4 | Verified: 1 593 passed, 1 skipped | 2026-11-04 · job `postgres-matrix`, cell `12` |
| 13.23 | Verified: 1 593 passed, 1 skipped | 2026-11-04 · cell `13` |
| 14.24 | Verified: 1 593 passed, 1 skipped | 2026-11-04 · cell `14` |
| 15.19 | Verified: 1 593 passed, 1 skipped | 2026-11-04 · cell `15` |
| 16.15 | Verified: 1 593 passed, 1 skipped | 2026-11-04 · cell `16` |
| 17.11 | Verified: 1 593 passed, 1 skipped | 2026-11-04 · cell `17` |
| 18.4, locale of the image | Verified: the default image of every run of the project | job `build` (Linux) runs it by default; the previous full-solution run on a development machine was 2026-11-03 |
| 18.4, ICU locale `tr-TR` | Verified: 1 593 passed, 1 skipped | 2026-11-04 · cell `18, ICU tr-TR` |
| 11 and older | Not verified: no `pgvector` image exists for these majors, so the test project cannot start | none |

pgvector 0.8 and later need PostgreSQL 13 or newer; on 12 the vector extension is the 0.7 series, the last that supports it.
Every migration and every query the store contracts reach ran on 12.

### SQL Server

`Tracon.SqlServer.IntegrationTests`, 1 558 tests. The servers run in an emulated amd64 container on this machine, which is
slower than a native one and changes no result.

| Server | Collation | Result | Run |
|---|---|---|---|
| 2017 | server default | **Partly verified.** Every migration applies and 1 555 of 1 557 tests pass. The two that fail pin the error number 2628, which the engine reports from 2019 on for a text longer than its column; 2017 reports 8152 instead. Tracon treats both numbers as a text-too-long fault, but no test proves it on 2017 | 2026-11-04, local only · no CI cell |
| 2019 | server default | Verified: 1 557 of 1 557. One case timed out in the first full run (a migration took longer than its 30-second command timeout on the emulated server under load) and passed when run alone | 2026-11-04 · cell `2019` |
| 2022 | server default | Verified: the default image of every run of the project | job `build` (Linux) runs it by default; the previous full-solution run on a development machine was 2026-11-03 |
| 2022 | `Turkish_CI_AS`, language 1055 | Verified: 1 558 of 1 558, once the test fixture waits for the server to finish applying the collation (the image applies it after the server first accepts connections; without the wait 1 518 of 1 557 cases failed on 2026-11-04) | 2026-11-04 · cell `2022, Turkish_CI_AS` |
| Azure SQL Database | not verified: it cannot be started as a container | none |

The two settings of the Turkish cell are given together: a server started with only the collation keeps English as its language, and a pooled connection fails when it is reset. The image also applies the collation after the server first accepts connections, by rebuilding the system databases and dropping every connection; a run waits until the server reports the collation before it starts.
`TenantAlphabetCollationTests` also asks each server which of the tenant identifiers over the allowed alphabet it
considers equal (none), on 2019, on 2022 and on 2022 with `Turkish_CI_AS`. Key columns that hold a name or an identifier
you choose are `Latin1_General_BIN2` on every server, so a server collation never decides which of two such values is the
same one; the [storage table](#storage-engines) says which columns.

### SQLite

`Tracon.Sqlite.IntegrationTests`, 1 639 tests (one skips unless a run names the engine version it expects). SQLite is a library, so its version is the version of the native file the
process loads. `Microsoft.Data.Sqlite` 10.0.14 brings 3.53.3. The rows below replace that file with the one a package of the
same family carried, and the project asks the engine for its version to be sure the replacement is the file that ran.

| SQLite | Result | Run |
|---|---|---|
| 3.53.3, the bundled library | Verified: 1 639 of 1 639 | 2026-11-04 · job `build` runs it by default |
| 3.44.0 | Verified: 1 639 of 1 639 | 2026-11-04 · job `sqlite-floor` |
| 3.41.2 | **Fails.** The case `A_revoked_key_stays_revoked_while_its_last_use_is_being_recorded` (a key revoked while its last use is written, 200 rounds) ends with `database is locked` after six minutes; 3.44.0 finishes it in 20 seconds. Only this case and the `1e300` case of the next row were run on this version | local only · no CI cell |
| 3.38.3 | **Fails.** The same case had not finished after eight minutes and was stopped; the `1e300` case of the next row fails | local only · no CI cell |
| 3.35.5 | **Partly verified.** Every migration applies and 1 636 of the 1 638 tests the project had then pass. The case above fails (`database is locked`), and a case that stores the text `1e300` and reads it back as a number fails: the engine returns 1.0000000000000005E+300, one unit in the last place off | local only · no CI cell |
| 3.33.0 and older | **Fails.** Migration `0030_job_handler_key` stops with `near "DROP": syntax error`: `ALTER TABLE … DROP COLUMN` needs 3.35 | local only (Linux arm64 container) · no CI cell |

No packaged build exists between 3.41.2 and 3.44.0, so the version at which the case above starts to pass is somewhere in
that range and 3.44.0 is the lowest verified one. The same table applies to a SQLite library your application loads itself (a system library, or
`SQLitePCLRaw.bundle_sqlite3`): what counts is its version.

### Operating system and culture

| Cell | Result | Run |
|---|---|---|
| Linux x64 | Not verified on a CI run: job `build` runs the whole solution on `ubuntu-latest` and has not run on GitHub. The database servers above are Linux containers, and the SQLite project ran in a Linux arm64 container (3.53.3 and 3.33.0) | job `build` |
| macOS (Darwin 27), Apple silicon | Verified: the whole solution runs on the development machine for every change | none (no CI leg) |
| Windows x64 | **Not verified.** Job `build` has a `windows-latest` leg that runs every project that does not need Docker; it has not run on GitHub | job `build`, Windows leg |
| Process culture `tr-TR`, time zone `America/New_York` | Verified: the whole solution, 16 275 tests, 2026-11-01; run again on 2026-11-04 for the core unit tests (6 543 of 6 543) and the SQLite project (1 638 passed, 1 skipped) | job `culture-matrix` (the projects that need no Docker) |
| Process culture `de-DE`, time zone `Australia/Lord_Howe` | Verified: the whole solution, 16 275 tests, 2026-11-01; run again on 2026-11-04 for the core unit tests (6 543 of 6 543 on the second run; the first run failed one lease case that passed in 3 runs alone and in 12 parallel runs) and the SQLite project (1 638 passed, 1 skipped) | job `culture-matrix` |

The culture rows set `LC_ALL` and `TZ` for the test process; .NET reads both on every operating system. The collation and
locale of the databases themselves are the PostgreSQL and SQL Server rows with `tr-TR` and `Turkish_CI_AS`.

## Agent Framework dependency versions

Seven packages depend on Microsoft Agent Framework or `Microsoft.Extensions.AI`.
These are the versions they declare.

| Package | Declared dependencies |
|---|---|
| `Tracon.Abstractions` | `Microsoft.Agents.AI.Abstractions` 1.26.0 · `Microsoft.Extensions.AI.Abstractions` 10.11.0 |
| `Tracon.Core` | `Microsoft.Agents.AI` 1.26.0 · `Microsoft.Agents.AI.Harness` 1.26.0 · `Microsoft.Extensions.AI` 10.11.0 · `Microsoft.Extensions.AI.Evaluation` 10.11.0 |
| `Tracon.Workflows` | `Microsoft.Agents.AI.Workflows` 1.26.0 |
| `Tracon.OpenAI` | `Microsoft.Agents.AI.OpenAI` 1.26.0 · `Microsoft.Extensions.AI.OpenAI` 10.11.0 |
| `Tracon.Azure` | `Microsoft.Extensions.AI.OpenAI` 10.11.0 |
| `Tracon.Testing.Contracts.Xunit` | `Microsoft.Agents.AI` 1.26.0 · `Microsoft.Extensions.AI` 10.11.0 |
| `Tracon.AspNetCore` | `Microsoft.Agents.AI.Hosting`, `.Hosting.AspNetCore`, and `.Hosting.A2A` at exactly a 1.26.0 pre-release · `.Hosting.OpenAI` at exactly a 1.26.0 alpha · `A2A.AspNetCore` at exactly `1.0.0-preview3` |

`Tracon.AspNetCore` is the only package that carries a pre-release Agent Framework
dependency, and it is the only one permitted to.

### A pre-release dependency is an exact pin

The five pre-release entries of `Tracon.AspNetCore` are exact ranges, such as
`[1.26.0-preview.261023.1]`. A pre-release package makes no compatibility promise
between its own previews, and this one broke: `Microsoft.Agents.AI.Hosting.AgentSessionStore`
left the Hosting assembly in `1.22.0-preview`, so a project that raised Hosting under
`1.0.0-preview.2` restored without a warning and failed at run time with a
`TypeLoadException`.

With an exact range NuGet reports the same situation at restore instead. A direct
reference to another version gives `NU1608`, and a package that needs another
version gives `NU1107`. To move to a newer Hosting preview, move to the Tracon release
that is built against it.

### A stable dependency is a floor, not a pin

Every other entry above is a minimum. NuGet reads `1.26.0` as `[1.26.0, )` and then resolves
the **lowest** version that satisfies every constraint in the graph, so a project that
asks for nothing else restores exactly the version listed. You get a different version
only when something asks for one: a direct `PackageReference` to a higher version, or
another package whose own floor is higher.

For a stable dependency Tracon declares **no upper bound**. A newer Agent Framework is
allowed to resolve, and Tracon will not stop it.

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
| `Tracon.OpenAI` | `openai` | OpenAI | API key | Chat Completions surface; rejects every `ModelBinding.ProviderSettings` key |
| `Tracon.OpenAI` | `openai-responses` | OpenAI | API key | Responses surface; registered with the official provider; rejects every `ProviderSettings` key |
| `Tracon.OpenAI` compatible registration | Caller-chosen name | Required or explicitly set by the caller | Optional for a local server | A second `{name}-responses` surface is opt-in because many compatible servers do not implement Responses; rejects every `ProviderSettings` key |
| `Tracon.Anthropic` | `anthropic` | Anthropic | API key | `max_tokens` is mandatory upstream; Tracon defaults the output limit to 4,096 |
| `Tracon.Google` | `google` | Gemini Developer API | API key | Supports Google-specific safety and thinking settings in `ModelBinding.ProviderSettings` |
| `Tracon.Azure` | `azure-openai` | No global default; resource endpoint is required | API key or consumer `TokenCredential` factory | `ModelBinding.Model` contains an Azure deployment name, not the underlying model name; rejects every `ProviderSettings` key |

The OpenAI-compatible path covers services such as OpenRouter and Groq and local
servers such as Ollama, LM Studio, and vLLM. Compatibility means the selected server
implements the surface you enable. It does not mean every server supports every
model feature.

A harness agent's hosted web search (`HarnessSettings.EnableWebSearch`, off by
default) is such a feature. The Chat Completions surface (`openai`, `azure-openai`,
and a compatible registration) accepts it only for search models and answers `400`
for the others; the Responses surface offers it to every model.

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
| Workflow definition | Create, edit, diff, roll back, inspect, run | Create, edit, diff, roll back, inspect, run | Define in code | Code workflows are read-only in the console and keep no version history |
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

The set has grown since the first release that carried it, and each growth was announced
this way. The decisions added after the first six (tenant separation, session ownership,
at-rest content protection, content inspection, request rate limiting and retention) are
`UnregisteredRolePolicies`, `UncheckedToolCalls`, `UnrestrictedTenantEgress`,
`UnverifiedTenantHeader`, `UnlimitedTenantWorkflows`, `UnkeyedAuditChain`, `UnauthenticatedLocalOperator` and `UnenforcedQuotaOnStoreFailure`; `CHANGELOG.md` gives the reason for
each.

Five more exist only for a deployment that declares more than one instance with `Tracon:Deployment:Instances`:
`InstanceLocalState`, `UnreconciledRuns`, `UnelectedBackgroundWork`, `InstanceLocalRateLimit` and
`InstanceLocalMcpTokens`. They are added the same way and each has its own `Accept` line. At the default of `1`
each is reported as not applicable, so a host that never declared a second instance starts exactly as before; a host that
declares two or more is asked them. Tracon cannot count replicas by itself, so a deployment that runs two and does not
declare it is not checked. The current list is the one table in [Make a production decision
required](/guides/embedding/#make-a-production-decision-required).

A decision can also gain a second answer without the set growing. `UncheckedToolCalls` was
answered only by a handler of your own; it is now answered as well by
`Tracon:Tools:RequireTenantPolicy` with a [tool policy](/concepts/tools/#per-tenant-tool-policy) for each
tenant. A host that already answers it with a handler keeps starting, and no host that
started before stops: this widens what satisfies the decision.

Read that cost before adopting the method. A deployment that wants its production
decisions pinned rather than re-asked should pin the package version, not avoid the
gate: the alternative is a new decision arriving in silence, which is the thing the
method exists to prevent.

## HTTP surface compatibility

The generated HTTP reference contains 182 management and OpenAI-compatible
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
| `AgentsRead` | Agent, skill, tool, and model catalog reads, and exporting definitions |
| `AgentsAdmin` | Agent, skill and MCP server definition writes, version rollback, and importing definitions (a file with workflows also needs `WorkflowsAdmin`) |
| `ExternalInvoke` | Explicitly exposed MCP-server and A2A invocation surfaces |
| `KnowledgeRead` | Knowledge collection listing and semantic search |
| `KnowledgeAdmin` | Knowledge document upload and deletion |
| `WorkflowsRead` | Workflow definitions, version history, graphs, checkpoints, and pending requests |
| `WorkflowsAdmin` | Workflow definition writes, version rollback and deletion; running still needs `RunsWrite` |
| `EvalsRead` | Eval suites, cases, runs, and online-evaluation summaries |
| `EvalsAdmin` | Eval suite and case writes; triggering a run still needs `RunsWrite` |
| `ExperimentsRead` | Experiments, results, and canary status |
| `ExperimentsAdmin` | Experiment writes, start and stop, and canary policy |
| `PlatformRead` | Tenant, quota, retention, scheduling, webhook, diagnostics, and provider-health reads |
| `PlatformAdmin` | Platform configuration writes and retention execution; with `SecurityAdmin`, granting a stored skill script in a multi-tenant host |
| `SecurityAdmin` | API keys, skill-script grants (reading the content hash to grant needs `AgentsRead` too), and MCP OAuth start |
| `AuditRead` | Audit-trail reads |

`SecurityAdmin` can create or extend authority. Grant it rarely. API keys are stored
hashed and the plaintext value is returned only at creation.

## Read next

- [Versions and upgrades](/reference/versioning/) — what a version bump is allowed to change
- [Choosing packages](/packages/) — which of these matrices apply to you
- [Configuration](/reference/configuration/) — every option the supported combinations expose
