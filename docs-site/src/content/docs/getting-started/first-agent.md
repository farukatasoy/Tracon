---
title: Your first agent
description: Build, run, inspect, and call your first AgentPrism agent from an empty folder in about five minutes.
sidebar:
  order: 2
---

Two ways in. The template writes a working application for you; the manual path shows
you what the template wrote.

## With the template

```bash
AGENTPRISM_VERSION=1.0.0-preview.N # replace N with the published preview
dotnet new install "AgentPrism.Templates@$AGENTPRISM_VERSION"
dotnet new agentprism-api -o MyAgents
cd MyAgents
```

Pinning the template version makes the generated package references reproducible.
The project template has five options:

| Option | Values | Default |
|---|---|---|
| `--persistence` | `memory`, `postgres`, `sqlite`, `sqlserver` | `memory` |
| `--provider` | `openai`, `anthropic`, `google`, `azure` | `openai` |
| `--ui` | `true`, `false` | `true` |
| `--AgentPrismVersion` | A NuGet version or version range | `*-*` (latest preview) |
| `--skipRestore` | `true`, `false` | `false` |

For example:

```bash
dotnet new agentprism-api -o MyAgents \
  --persistence postgres \
  --provider openai \
  --ui true \
  --AgentPrismVersion "$AGENTPRISM_VERSION"
```

Set your key — it never goes in a file that gets committed:

```bash
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-…"
dotnet run
```

Open the address `dotnet run` prints, with `/agentprism` on the end — the
template listens on `http://localhost:5081` by default.

## By hand

```bash
dotnet new web -o MyAgents
cd MyAgents
dotnet add package AgentPrism --prerelease
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-…"
```

```csharp title="Program.cs"
using AgentPrism;

var builder = WebApplication.CreateBuilder(args);

var agentPrism = builder.AddAgentPrism()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UseUI();

agentPrism.AddAgent(new AgentDefinition
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

app.MapAgentPrism("/agentprism");
app.Run();
```

```bash
dotnet run
```

:::note[Why the model name is a blank]
AgentPrism ships no built-in model list and pins no model name. Provider catalogues
change faster than a NuGet release, and a hard-coded name would be wrong within
months. Take the current name from your provider's documentation, or put it in
`appsettings.json` under `AgentPrism:Providers:OpenAI:DefaultModel`.
:::

## Run it

**In the console.** Open `/agentprism`, pick **Playground**, choose `support`, and
send a message. The reply streams in; tool calls appear as cards with their arguments
and results.

**Over HTTP.** The same run, as a server-sent event stream:

```bash
curl -N -X POST http://localhost:5081/agentprism/api/agents/support/run \
     -H 'Content-Type: application/json' \
     -d '{"message":"Where is order 4182?"}'
```

**From an OpenAI client.** The compatible endpoint accepts the familiar wire format.
Point the client at AgentPrism, provide its authentication, and use the agent name as
the `model`:

```bash
curl -X POST http://localhost:5081/agentprism/v1/responses \
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
curl http://localhost:5081/agentprism/api/runs
curl http://localhost:5081/agentprism/api/runs/{runId}
curl -N http://localhost:5081/agentprism/api/runs/{runId}/events
```

Nothing extra was configured to make that happen. Recording can be disabled. A store
failure is also best-effort: it is logged and the agent still runs, so observability
cannot take down product functionality.

## What you have

An agent defined in code, a console, and a recorded history — with no database. Every
store is in memory, so all of it ends when the process does.

## Next

- [Adding a tool](/AgentPrism/getting-started/tools/) — let the agent do something
- [Persistence](/AgentPrism/getting-started/persistence/) — make it survive a restart
- [Securing the endpoints](/AgentPrism/getting-started/security/) — before it leaves your machine
