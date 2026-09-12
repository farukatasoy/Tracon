---
title: Your first agent
description: Build, run, inspect, and call your first Tracon agent from an empty folder in about five minutes.
sidebar:
  order: 2
---

Two ways in. The template writes a working application for you; the manual path shows
you what the template wrote.

## With the template

```bash
TRACON_VERSION=1.0.0-preview.N # replace N with the published preview
dotnet new install "Tracon.Templates@$TRACON_VERSION"
dotnet new tracon-api -o MyAgents
cd MyAgents
```

Pinning the template version makes the generated package references reproducible.
The project template has five options:

| Option | Values | Default |
|---|---|---|
| `--persistence` | `memory`, `postgres`, `sqlite`, `sqlserver` | `memory` |
| `--provider` | `openai`, `anthropic`, `google`, `azure` | `openai` |
| `--ui` | `true`, `false` | `true` |
| `--TraconVersion` | A NuGet version or version range | `*-*` (latest preview) |
| `--skipRestore` | `true`, `false` | `false` |

For example:

```bash
dotnet new tracon-api -o MyAgents \
  --persistence postgres \
  --provider openai \
  --ui true \
  --TraconVersion "$TRACON_VERSION"
```

Set your key — it never goes in a file that gets committed:

```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "sk-…"
dotnet run
```

Open the address `dotnet run` prints, with `/tracon` on the end — the
template listens on `http://localhost:5081` by default.

The first build also writes `AGENTS.md` at the root of your repository: the
Tracon capability map, for a coding agent working in the project. An
existing file is never overwritten, and
[the property that writes it](/troubleshooting/#agentsmd-does-not-appear)
can be removed from the project file.

## By hand

:::caution[Not published yet]
No Tracon version has been pushed to NuGet or npm yet, so this command
does not resolve. Until the first release, reference the projects from a
clone of the repository.
:::

```bash
dotnet new web -o MyAgents
cd MyAgents
dotnet add package Tracon --prerelease
dotnet user-secrets init
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "sk-…"
```

```csharp title="Program.cs"
using Tracon;

var builder = WebApplication.CreateBuilder(args);

var tracon = builder.AddTracon()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UseUI();

tracon.AddAgent(new AgentDefinition
{
    Name = "support",
    DisplayName = "Support Assistant",
    Description = "Answers order and shipping questions.",
    Instructions = "You are a support assistant. Answer briefly and clearly.",
    Model = new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = "…",   // today's model name, from your provider's documentation
    },
});

var app = builder.Build();

app.MapTracon("/tracon");
app.Run();
```

This first agent uses a chat model only. To let a later agent generate stored image
attachments, add `UseOpenAIImages(...)`, set `Tracon:Images:Enabled`, and choose
an image model explicitly. An image model is not inferred from this agent's chat
model; see [image generation providers](/guides/model-providers/#image-generation-providers).

```bash
dotnet run
```

:::note[Why the model name is a blank]
Tracon ships no built-in model list and pins no model name. Provider catalogues
change faster than a NuGet release, and a hard-coded name would be wrong within
months. Take the current name from your provider's documentation, or put it in
`appsettings.json` under `Tracon:Providers:OpenAI:DefaultModel`.
:::

## Run it

**In the console.** Open `/tracon`, pick **Playground**, choose `support`, and
send a message. The reply streams in; tool calls appear as cards with their arguments
and results.

**Over HTTP.** The same run, as a server-sent event stream:

```bash
curl -N -X POST http://localhost:5081/tracon/api/agents/support/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"Where is order 4182?"}'
```

**From an OpenAI client.** The compatible endpoint accepts the familiar wire format.
Point the client at Tracon, provide its authentication, and use the agent name as
the `model`:

```bash
curl -X POST http://localhost:5081/tracon/v1/responses \
     -H 'Content-Type: application/json' \
     -d '{"model":"support","input":"Where is order 4182?"}'
```

Here `model` is the *agent* name. Which model it actually calls is the agent's
business, not the caller's.

## Look at what happened

Run recording is on by default for agents resolved through the catalog. In the
console, open **Runs**: status, duration, token counts, cost when pricing is
configured, and the ordered event stream. Over HTTP it is the same data:

```bash
curl http://localhost:5081/tracon/api/runs
curl http://localhost:5081/tracon/api/runs/{runId}
curl -N http://localhost:5081/tracon/api/runs/{runId}/events
```

Nothing extra was configured to make that happen. Recording can be disabled. A store
failure is also best-effort: it is logged and the agent still runs, so observability
cannot take down product functionality.

## What you have

An agent defined in code, a console, and a recorded history — with no database. Every
store is in memory, so all of it ends when the process does.

## Read next

- [Adding a tool](/getting-started/tools/) — let the agent do something
- [Persistence](/getting-started/persistence/) — make it survive a restart
- [Securing the endpoints](/getting-started/security/) — before it leaves your machine
