---
title: Your first agent
description: Build a Tracon host from an authorized source checkout, configure a model, and inspect your first recorded agent run.
sidebar:
  order: 2
---

Build an ASP.NET Core host, register a model-backed agent, then inspect its run
in the embedded console. MAF executes the agent; Tracon supplies the catalog,
HTTP endpoints, and default-on recording around it.

:::caution[Repository access required]
Tracon packages and templates are not published yet. This guide requires an
existing source checkout that you are authorized to access. If you do not have
access, start with the [capability map](/capabilities/) and
[architecture](/concepts/) to evaluate the design.
:::

## Prerequisites

- An authorized checkout of the Tracon repository.
- The .NET SDK selected by the repository's `global.json` and Node.js for the
  embedded console build. See the checkout's README for development prerequisites.
- An OpenAI API key and a chat model available to that account. The model request
  is sent to your configured provider and can incur provider charges.

<span id="with-the-template"></span>

## Build from source

Run these commands from the root of the Tracon checkout. They create a sibling
application and reference the three source projects it needs.

```bash
dotnet new web -o ../MyAgents
cd ../MyAgents
dotnet add reference ../Tracon/src/Tracon.AspNetCore/Tracon.AspNetCore.csproj
dotnet add reference ../Tracon/src/Tracon.OpenAI/Tracon.OpenAI.csproj
dotnet add reference ../Tracon/src/Tracon.UI/Tracon.UI.csproj
dotnet user-secrets init
```

The commands assume the checkout directory is named `Tracon`. Use its actual
relative path if you named it differently. The console assets build with the UI
project; no separate console server is needed.

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

The source template remains available in the repository for readers who want to
inspect its generated application. A published-template install command will be
added when a release is available.

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
