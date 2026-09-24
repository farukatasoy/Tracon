---
title: Your first agent
description: Install the Tracon template, configure a model, run an agent, and inspect its recorded execution in the embedded console.
sidebar:
  order: 2
---

Build an ASP.NET Core host, register a model-backed agent, then inspect its run
in the embedded console. MAF executes the agent; Tracon supplies the catalog,
HTTP endpoints, and default-on recording around it.

:::note[Preview release]
This guide uses `1.0.0-preview.3`. Preview packages require explicit selection;
pin the exact version when you need a reproducible build.
:::

## Prerequisites

- The .NET 10 SDK.
- An OpenAI API key and a chat model available to that account. The model request
  is sent to your configured provider and can incur provider charges.

<span id="with-the-template"></span>

## Create the host

Install the published template, then create a project. The template pins every
Tracon package to the same version as the template package.

```bash
dotnet new install Tracon.Templates@1.0.0-preview.3
dotnet new tracon-api -n MyAgents
cd MyAgents
```

Use `--persistence`, `--provider`, and `--ui` to change the generated project.
Run `dotnet new tracon-api -h` to see the available values. The console is
embedded in the host; no separate console server is needed.

Store your provider settings in the application's development secrets. Replace
the example values with your key and a model identifier available to your account.

```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Tracon:Providers:OpenAI:DefaultModel" "YOUR_CHAT_MODEL"
```

<span id="by-hand"></span>

## Register the agent

Replace the generated `Program.cs` with the following:

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
        Model = builder.Configuration["Tracon:Providers:OpenAI:DefaultModel"]
            ?? throw new InvalidOperationException("Configure an OpenAI chat model."),
    },
});

var app = builder.Build();

app.MapTracon("/tracon");
app.Run();
```

This first agent uses a chat model only. To let a later agent generate stored image
attachments, add `UseOpenAIImages(...)`, set `Tracon:Images:Enabled`, and choose
an image model explicitly — one the provider serves for image generation, which is
not always the same family as its chat models. An image model is not inferred from
this agent's chat model; see
[image generation providers](/guides/model-providers/#image-generation-providers).

```bash
dotnet run --urls http://localhost:5081
```

The host listens on `http://localhost:5081`. Open
`http://localhost:5081/tracon` for the console. Provider model names are explicit
configuration: Tracon does not ship a built-in model list.

The template source is public at
[github.com/farukatasoy/Tracon](https://github.com/farukatasoy/Tracon/tree/main/src/Tracon.Templates/content/Tracon.Starter).

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

No additional recording registration is needed for this catalog-resolved agent. Recording can be disabled. A store
failure is also best-effort: it is logged and the agent still runs, so observability
cannot take down product functionality.

## What you have

An agent defined in code, a console, and a recorded history — with no database. Every
store is in memory, so all of it ends when the process does.

## Read next

- [Adding a tool](/getting-started/tools/) — register a C# method the model can call.
- [Persistence](/getting-started/persistence/) — retain definitions and run records across restarts.
- [Securing the endpoints](/getting-started/security/) — configure authorization before exposing the host.
