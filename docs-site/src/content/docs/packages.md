---
title: Choosing packages
description: What each of the 20 packages does, which come with the meta package, and what enters your dependency graph.
slug: packages
---

Twenty packages. Take the meta package for the common set, or pick individually
when you care about what enters your dependency graph.

## Licensing in one line

`Tracon.Abstractions`, `Tracon.Testing.Contracts.Xunit` and
`Tracon.Templates` are MIT; every other package is PolyForm Small Business
1.0.0, which is free below 100 people and 1,000,000 USD revenue. Writing an
extension, testing it, and owning what `dotnet new` generates therefore never
needs a commercial licence. Details and the reasoning: [Licensing](/reference/licensing/).

## The meta package

:::note[Preview package]
The current release is `1.0.0-preview.2`. The command below selects it from
NuGet; pin the exact version for reproducible builds.
:::

```bash
dotnet add package Tracon --prerelease
```

Brings runtime, PostgreSQL persistence, the OpenAI provider, the HTTP API, workflows,
MCP, and the console — eight packages counting the two that come transitively.

| Package | What it does |
|---|---|
| `Tracon.Abstractions` | Contracts. No provider, no web framework, no database |
| `Tracon.Core` | Catalog, definition compiler, tool registry, run recording, the startup composition gates, and the build-time analyzer |
| `Tracon.PostgreSql` | Persistence, and the vector store behind knowledge search |
| `Tracon.OpenAI` | OpenAI, plus any OpenAI-compatible endpoint — OpenRouter, Groq, or a self-hosted engine such as Ollama or vLLM |
| `Tracon.AspNetCore` | The HTTP API and the access layers |
| `Tracon.Workflows` | Multi-agent workflows, checkpoints, human input |
| `Tracon.Mcp` | Tools discovered from remote MCP servers |
| `Tracon.UI` | The embedded console |

## Not in the meta package

Add these when you need them.

| Package | Add it when |
|---|---|
| `Tracon.SqlServer` | You run SQL Server instead of PostgreSQL |
| `Tracon.Sqlite` | One node, or durable local development |
| `Tracon.Anthropic` | You call Claude models |
| `Tracon.Google` | You call Gemini models |
| `Tracon.Azure` | You call Azure OpenAI deployments |
| `Tracon.Voice` | You need speech synthesis, transcription, or live conversation |
| `Tracon.Testing` | You write tests against agents — fakes, not mocks |
| `Tracon.Testing.Contracts.Xunit` | You write your own store (`IRunStore` or another), `IModelProvider`, `IRunJudge`, `IAgentSource`, `IJobHandler`, a custom tool, or your own `IToolArgumentsValidator`/`IToolAuthorizationHandler`, and want the behavior contract the shipped implementations run |
| `Tracon.Templates` | `dotnet new tracon-api` |

`Tracon.OpenAI`, `Tracon.Azure`, and `Tracon.Google` also expose optional
image-generator registrations. They reuse their chat provider's authenticated client,
but image generation stays off until you configure `Tracon:Images`; see [model
providers](/guides/model-providers/#image-generation-providers).

## Calling Tracon from elsewhere

These are not runtime packages you host Tracon with — they call a running
Tracon instance, from a separate application or from a terminal.

| Package | What it does |
|---|---|
| `Tracon.Client` | A typed HTTP client for the management API, generated from the OpenAPI document. Every endpoint that answers with Server-Sent Events also gets a `...StreamAsync` method yielding one frame at a time. Takes no Tracon package and no NuGet package beyond `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `Tracon.Cli` | The `tracon` global tool (`dotnet tool install -g Tracon.Cli`): `migrate` and `migrate status` apply pending migrations without starting the application; `state-check` reports read-only whether this build can still read the session and checkpoint state already stored; `health` reads model provider health over HTTP through `Tracon.Client`; `eval` triggers an eval suite, polls it to completion, and gates a build on the result; `agent-skill` writes the gate skill a coding agent's harness loads before it writes Tracon code |

See the [CLI guide](/guides/cli/) for setup and every command.

## Calling Tracon from TypeScript

`@tracon/client` is not a NuGet package — it is the npm counterpart to
`Tracon.Client`, generated from the same OpenAPI document for callers that
are not on .NET.

```bash
npm install @tracon/client
```

| Package | What it does |
|---|---|
| `@tracon/client` | A typed TypeScript client for the management API, built on `openapi-fetch` — its only runtime dependency. Exports an incremental `readSse` decoder for the endpoints that stream |

Same OpenAPI document, same version number as every package above — `@tracon/client`
and `Tracon.Client` are cut from the same `v*` git tag, so a matching pair always
describes the identical set of operations. There is no separate npm version scheme.
What differs is the ecosystem: it ships from a browser or Node.js process instead of
a .NET one, and it is not part of the eight AOT-compatible packages or the twenty
NuGet packages counted above. See the [TypeScript client guide](/guides/typescript-client/)
for setup, the error model, streaming, and what it deliberately does not cover.

## Picking a database

All three implement the same contracts and are verified against the same shared
contract suite. Switching the store registration is simple; provider capabilities
and production topology are not identical.

| | Vector search | Notes |
|---|---|---|
| `Tracon.PostgreSql` | **yes** | The default. `pgvector` is only needed when `EnableKnowledge` is turned on; no ORM |
| `Tracon.SqlServer` | no | Knowledge endpoints answer `501` |
| `Tracon.Sqlite` | no | Ships a native library, so not AOT-compatible |

All three also offer an opt-in `runs_v1` read-only view (`EnableReadViews`) for
querying run data with your own SQL or an EF Core keyless entity — see
[Read contract views](/reference/read-views/).

## Picking a model provider

Several can be registered at once, and an agent chooses by provider name.

| Package | Provider names |
|---|---|
| `Tracon.OpenAI` | `openai`, `openai-responses`, and any name you register with `UseOpenAICompatible()` |
| `Tracon.Anthropic` | `anthropic` |
| `Tracon.Google` | `google` |
| `Tracon.Azure` | `azure-openai` |

:::caution[Azure: the deployment name is not the model name]
Azure calls the **deployment** name, chosen by whoever provisioned the resource. The
same model can be deployed under two names in two resources, and a wrong name produces
`404` rather than "model not found".
:::

Tracon ships **no built-in model list**. The catalogue comes from your
configuration and is not a validation list — a name not listed there still works.
Provider catalogues change faster than a NuGet release.

### Local and self-hosted models

`UseOpenAICompatible(name, …)` is part of `Tracon.OpenAI` — not a separate
package — and points at any server that speaks the OpenAI Chat Completions API,
including one running on your own hardware:

```csharp
builder.AddTracon()
       .UseOpenAICompatible("ollama", o =>
       {
           o.Endpoint = new Uri("http://localhost:11434/v1");
           // No ApiKey — local servers do not ask for one.
       });
```

The same call works for vLLM and LM Studio. The model call then stays inside your
network: no cloud account and no third-party endpoint on that path. This is the
option for regulated or air-gapped environments that cannot send prompts to a
third-party API. What the rest of the installation sends outward — exporters,
webhooks, other configured providers — stays your host's decision; see [what Tracon
deliberately is not](/getting-started/#what-it-deliberately-is-not).

## Trimming and native AOT

Eight runtime packages make the trimming and Native AOT compatibility promise:
`Tracon.Abstractions`, `Core`, `PostgreSql`, `OpenAI`, `Anthropic`, `Google`,
`Azure`, and `Voice`. The following do not:

| Package | Why not |
|---|---|
| `Tracon.AspNetCore` | Minimal API delegate routing uses reflection |
| `Tracon.UI` | Embedded asset scanning |
| `Tracon.Sqlite` | `SQLitePCLRaw` carries a native library |
| `Tracon.SqlServer` | Measured clean, but not verified against a live query — the promise is withheld rather than guessed |
| `Tracon.Mcp` | MCP schema and serialization paths use runtime reflection |
| `Tracon.Workflows` | The MAF workflow engine uses runtime reflection |
| `Tracon.Testing` | Test-host infrastructure does not make an AOT promise |
| `Tracon.Testing.Contracts.Xunit` | The build-time contract-coverage check reflects over the consumer's test assembly |
| `Tracon` | The meta package brings non-AOT hosting packages into the graph |
| `Tracon.Client` | The generated client's JSON calls are hand-wired to a source-generated `JsonSerializerContext` (no runtime reflection), but the code generator hardcodes generic `JsonSerializer` overloads the trim/AOT analyzer flags regardless — the promise is withheld rather than guessed |

In `Tracon.Core` the only reflection is in `AddTool(Delegate)` and
`AddToolsFrom<T>()`, both annotated so the warning reaches you. `AddGeneratedTools()`
is the recommended path — filled in at compile time, no reflection at all.

## Pre-release dependencies

`Tracon.AspNetCore` depends on `Microsoft.Agents.AI.Hosting` (preview) and
`.Hosting.OpenAI` (alpha). Every pre-release dependency is deliberately concentrated
there, so a consumer using only the runtime never takes one.

The same package also carries the OpenAPI document for the endpoints it serves,
under `buildTransitive/tracon.json`. It is a static file and adds no
dependency; `Tracon.LocalReference.md` names its path so a coding agent can
read the HTTP surface without leaving the machine. The document describes the
surface `MapTracon()` always serves — opt-in endpoints such as A2A exposure,
the diagnostics route, and the voice stream are served but not listed.

Tracon publishes as `1.0.0-preview.N` until those two go GA. See [Versions and
upgrades](/reference/versioning/) for pinning the whole package family and
upgrading safely between previews.

## API stability

Every package's public surface is tracked by a Roslyn analyzer (`PublicAPI.Shipped.txt`
/ `PublicAPI.Unshipped.txt` per package). Adding, removing, or changing a public
member without updating that file **fails the build** — so an accidental surface
change cannot ship silently, and every intentional one is visible in the commit
that made it.

Two projects are deliberately excluded: `Tracon.Generators` is a `netstandard2.0`
build-time analyzer carried inside the Core package, not a separate NuGet package;
`Tracon.Templates` is a .NET 10 `dotnet new` content package, not runtime API.
The project it generates references the Tracon packages at **the template
package's own version**, so a template and the runtime it writes always come
from the same release. Pass `--TraconVersion x.y.z` to choose another one.

## What does not enter your graph

- No ORM, and no Entity Framework. Each SQL provider's `Options.DataSource` field
  accepts a `DbDataSource` you built yourself — for example to share a connection
  pool with your own EF Core `DbContext` — but the field's type is the framework's
  own `System.Data.Common.DbDataSource`, not an ORM type; see
  [Two connection planes: EF Core and Tracon](/guides/ef-core/)
- No OpenAPI package — route metadata only, so your own `AddOpenApi()` produces the
  document
- No JavaScript build. The console ships pre-built and Brotli-compressed inside the
  assembly; there is no `node_modules` in your project
- No `Azure.Identity` unless you use managed identity — the Azure package binds to the
  `Azure.Core` abstraction and lets you choose the credential

## What enters your graph if you opt in

Choosing a provider package pulls its official SDK. One of them brings extra
transitive weight beyond the SDK itself, worth naming up front:

- `Tracon.Google` — Google's official Gemini SDK (`Google.GenAI`) carries
  `Newtonsoft.Json`, `System.Management`, and `System.CodeDom` through
  `Google.Apis.Auth`. A consumer who does not reference `Tracon.Google`
  gets none of it.

## Reference

[Every public type](/api/), generated from the shipped assemblies and their
XML documentation. For pinning versions and upgrading, see [Versions and
upgrades](/reference/versioning/).

## Read next

- [Complete capability map](/capabilities/) — what each package adds, feature by feature
- [Compatibility matrices](/reference/compatibility/) — target frameworks, stores, and AOT support per package
- [Versions and upgrades](/reference/versioning/) — how to pin what you just chose
