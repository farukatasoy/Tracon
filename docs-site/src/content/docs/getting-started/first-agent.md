---
title: Your first agent
description: From an empty folder to a running agent with a console, in about five minutes.
sidebar:
  order: 2
---

Two ways in. The template writes a working application for you; the manual path shows
you what the template wrote.

## With the template

```bash
dotnet new install AgentPrism.Templates
dotnet new agentprism-api -o MyAgents
cd MyAgents
```

The template takes three options, all with sensible defaults:

```bash
dotnet new agentprism-api -o MyAgents \
  --persistence postgres \   # postgres | sqlserver | sqlite | none
  --provider openai \        # openai | anthropic | google | azure
  --ui true
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
dotnet add package AgentPrism
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

**From an OpenAI client.** The compatible endpoints mean an existing client only
needs a different base address:

```bash
curl -X POST http://localhost:5081/agentprism/v1/responses \
     -H 'Content-Type: application/json' \
     -d '{"model":"support","input":"Where is order 4182?"}'
```

Here `model` is the *agent* name. Which model it actually calls is the agent's
business, not the caller's.

## Look at what happened

Every one of those runs was recorded. In the console, open **Runs**: status, duration,
token counts, cost when pricing is configured, and the full event stream in order.
Over HTTP it is the same data:

```bash
curl http://localhost:5081/agentprism/api/runs
curl http://localhost:5081/agentprism/api/runs/{runId}
curl -N http://localhost:5081/agentprism/api/runs/{runId}/events
```

Nothing was configured to make that happen. There is no code path that runs an agent
without recording it.

## What you have

An agent defined in code, a console, and a recorded history — with no database. Every
store is in memory, so all of it ends when the process does.

## Next

- [Adding a tool](/AgentPrism/getting-started/tools/) — let the agent do something
- [Persistence](/AgentPrism/getting-started/persistence/) — make it survive a restart
- [Securing the endpoints](/AgentPrism/getting-started/security/) — before it leaves your machine
