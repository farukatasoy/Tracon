---
title: Choosing packages
description: What each of the 20 packages does, which come with the meta package, and what enters your dependency graph.
slug: packages
---

Twenty packages. Take the meta package for the common set, or pick individually
when you care about what enters your dependency graph.

## The meta package

```bash
dotnet add package AgentPrism --prerelease
```

Brings runtime, PostgreSQL persistence, the OpenAI provider, the HTTP API, workflows,
MCP, and the console — eight packages counting the two that come transitively.

| Package | What it does |
|---|---|
| `AgentPrism.Abstractions` | Contracts. No provider, no web framework, no database |
| `AgentPrism.Core` | Catalog, definition compiler, tool registry, run recording, and the build-time analyzer |
| `AgentPrism.PostgreSql` | Persistence, and the vector store behind knowledge search |
| `AgentPrism.OpenAI` | OpenAI, plus any OpenAI-compatible endpoint — OpenRouter, Groq, or a self-hosted engine such as Ollama or vLLM |
| `AgentPrism.AspNetCore` | The HTTP API and the access layers |
| `AgentPrism.Workflows` | Multi-agent workflows, checkpoints, human input |
| `AgentPrism.Mcp` | Tools discovered from remote MCP servers |
| `AgentPrism.UI` | The embedded console |

## Not in the meta package

Add these when you need them.

| Package | Add it when |
|---|---|
| `AgentPrism.SqlServer` | You run SQL Server instead of PostgreSQL |
| `AgentPrism.Sqlite` | One node, or durable local development |
| `AgentPrism.Anthropic` | You call Claude models |
| `AgentPrism.Google` | You call Gemini models |
| `AgentPrism.Azure` | You call Azure OpenAI deployments |
| `AgentPrism.Voice` | You need speech synthesis, transcription, or live conversation |
| `AgentPrism.Testing` | You write tests against agents — fakes, not mocks |
| `AgentPrism.Testing.Contracts.Xunit` | You write your own store (`IRunStore` or another) or your own `IModelProvider`, and want the behavior contract the shipped implementations run |
| `AgentPrism.Templates` | `dotnet new agentprism-api` |

`AgentPrism.OpenAI`, `AgentPrism.Azure`, and `AgentPrism.Google` also expose optional
image-generator registrations. They reuse their chat provider's authenticated client,
but image generation stays off until you configure `AgentPrism:Images`; see [model
providers](/guides/model-providers/#image-generation-providers).

## Calling AgentPrism from elsewhere

These are not runtime packages you host AgentPrism with — they call a running
AgentPrism instance, from a separate application or from a terminal.

| Package | What it does |
|---|---|
| `AgentPrism.Client` | A typed HTTP client for the management API, generated from the OpenAPI document. Takes no AgentPrism package and no NuGet package beyond `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `AgentPrism.Cli` | The `agentprism` global tool (`dotnet tool install -g AgentPrism.Cli`): `migrate` and `migrate status` apply pending migrations without starting the application; `health` reads model provider health over HTTP through `AgentPrism.Client` |

See the [CLI guide](/guides/cli/) for setup and every command.

## Calling AgentPrism from TypeScript

`@agentprism/client` is not a NuGet package — it is the npm counterpart to
`AgentPrism.Client`, generated from the same OpenAPI document for callers that
are not on .NET.

```bash
npm install @agentprism/client
```

| Package | What it does |
|---|---|
| `@agentprism/client` | A typed TypeScript client for the management API, built on `openapi-fetch` — its only runtime dependency |

Same OpenAPI document, same version number as every package above — `@agentprism/client`
and `AgentPrism.Client` are cut from the same `v*` git tag, so a matching pair always
describes the identical set of operations. There is no separate npm version scheme.
What differs is the ecosystem: it ships from a browser or Node.js process instead of
a .NET one, and it is not part of the eight AOT-compatible packages or the twenty
NuGet packages counted above. See the [TypeScript client guide](/guides/typescript-client/)
for setup, the error model, and what it deliberately does not cover.

## Picking a database

All three implement the same contracts and are verified against the same shared
contract suite. Switching the store registration is simple; provider capabilities
and production topology are not identical.

| | Vector search | Notes |
|---|---|---|
| `AgentPrism.PostgreSql` | **yes** | The default. `pgvector` is only needed when `EnableKnowledge` is turned on; no ORM |
| `AgentPrism.SqlServer` | no | Knowledge endpoints answer `501` |
| `AgentPrism.Sqlite` | no | Ships a native library, so not AOT-compatible |

## Picking a model provider

Several can be registered at once, and an agent chooses by provider name.

| Package | Provider names |
|---|---|
| `AgentPrism.OpenAI` | `openai`, `openai-responses`, and any name you register with `UseOpenAICompatible()` |
| `AgentPrism.Anthropic` | `anthropic` |
| `AgentPrism.Google` | `google` |
| `AgentPrism.Azure` | `azure-openai` |

:::caution[Azure: the deployment name is not the model name]
Azure calls the **deployment** name, chosen by whoever provisioned the resource. The
same model can be deployed under two names in two resources, and a wrong name produces
`404` rather than "model not found".
:::

AgentPrism ships **no built-in model list**. The catalogue comes from your
configuration and is not a validation list — a name not listed there still works.
Provider catalogues change faster than a NuGet release.

### Local and self-hosted models

`UseOpenAICompatible(name, …)` is part of `AgentPrism.OpenAI` — not a separate
package — and points at any server that speaks the OpenAI Chat Completions API,
including one running on your own hardware:

```csharp
builder.AddAgentPrism()
       .UseOpenAICompatible("ollama", o =>
       {
           o.Endpoint = new Uri("http://localhost:11434/v1");
           // No ApiKey — local servers do not ask for one.
       });
```

The same call works for vLLM and LM Studio. Nothing about the request leaves your
network: no cloud account, no external endpoint, no data leaving the machine that
runs it. This is the option for regulated or air-gapped environments that cannot
send prompts to a third-party API — see [what AgentPrism deliberately is
not](/getting-started/#what-it-deliberately-is-not).

## Trimming and native AOT

Eight runtime packages make the trimming and Native AOT compatibility promise:
`AgentPrism.Abstractions`, `Core`, `PostgreSql`, `OpenAI`, `Anthropic`, `Google`,
`Azure`, and `Voice`. The following do not:

| Package | Why not |
|---|---|
| `AgentPrism.AspNetCore` | Minimal API delegate routing uses reflection |
| `AgentPrism.UI` | Embedded asset scanning |
| `AgentPrism.Sqlite` | `SQLitePCLRaw` carries a native library |
| `AgentPrism.SqlServer` | Measured clean, but not verified against a live query — the promise is withheld rather than guessed |
| `AgentPrism.Mcp` | MCP schema and serialization paths use runtime reflection |
| `AgentPrism.Workflows` | The MAF workflow engine uses runtime reflection |
| `AgentPrism.Testing` | Test-host infrastructure does not make an AOT promise |
| `AgentPrism.Testing.Contracts.Xunit` | The build-time contract-coverage check reflects over the consumer's test assembly |
| `AgentPrism` | The meta package brings non-AOT hosting packages into the graph |
| `AgentPrism.Client` | The generated client's JSON calls are hand-wired to a source-generated `JsonSerializerContext` (no runtime reflection), but the code generator hardcodes generic `JsonSerializer` overloads the trim/AOT analyzer flags regardless — the promise is withheld rather than guessed |

In `AgentPrism.Core` the only reflection is in `AddTool(Delegate)` and
`AddToolsFrom<T>()`, both annotated so the warning reaches you. `AddGeneratedTools()`
is the recommended path — filled in at compile time, no reflection at all.

## Pre-release dependencies

`AgentPrism.AspNetCore` depends on `Microsoft.Agents.AI.Hosting` (preview) and
`.Hosting.OpenAI` (alpha). Every pre-release dependency is deliberately concentrated
there, so a consumer using only the runtime never takes one.

The same package also carries the OpenAPI document for the endpoints it serves,
under `buildTransitive/agentprism.json`. It is a static file and adds no
dependency; `AgentPrism.LocalReference.md` names its path so a coding agent can
read the HTTP surface without leaving the machine. The document describes the
surface `MapAgentPrism()` always serves — opt-in endpoints such as A2A exposure,
the diagnostics route, and the voice stream are served but not listed.

AgentPrism publishes as `1.0.0-preview.N` until those two go GA. See [Versions and
upgrades](/reference/versioning/) for pinning the whole package family and
upgrading safely between previews.

## API stability

Every package's public surface is tracked by a Roslyn analyzer (`PublicAPI.Shipped.txt`
/ `PublicAPI.Unshipped.txt` per package). Adding, removing, or changing a public
member without updating that file **fails the build** — so an accidental surface
change cannot ship silently, and every intentional one is visible in the commit
that made it.

Two projects are deliberately excluded: `AgentPrism.Generators` is a `netstandard2.0`
build-time analyzer carried inside the Core package, not a separate NuGet package;
`AgentPrism.Templates` is a .NET 10 `dotnet new` content package, not runtime API.

## What does not enter your graph

- No ORM, and no Entity Framework
- No OpenAPI package — route metadata only, so your own `AddOpenApi()` produces the
  document
- No JavaScript build. The console ships pre-built and Brotli-compressed inside the
  assembly; there is no `node_modules` in your project
- No `Azure.Identity` unless you use managed identity — the Azure package binds to the
  `Azure.Core` abstraction and lets you choose the credential

## What enters your graph if you opt in

Choosing a provider package pulls its official SDK. One of them brings extra
transitive weight beyond the SDK itself, worth naming up front:

- `AgentPrism.Google` — Google's official Gemini SDK (`Google.GenAI`) carries
  `Newtonsoft.Json`, `System.Management`, and `System.CodeDom` through
  `Google.Apis.Auth`. A consumer who does not reference `AgentPrism.Google`
  gets none of it.

## Reference

[Every public type](/api/), generated from the shipped assemblies and their
XML documentation. For pinning versions and upgrading, see [Versions and
upgrades](/reference/versioning/).

## Read next

- [Complete capability map](/capabilities/) — what each package adds, feature by feature
- [Compatibility matrices](/reference/compatibility/) — target frameworks, stores, and AOT support per package
- [Versions and upgrades](/reference/versioning/) — how to pin what you just chose
