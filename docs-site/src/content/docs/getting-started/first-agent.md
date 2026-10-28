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
This guide uses `1.0.0-preview.6`. Preview packages require explicit selection;
pin the exact version when you need a reproducible build.
:::

## Prerequisites

- The .NET 10 SDK.
- An OpenAI API key and a chat model available to that account. The model request
  is sent to your configured provider and can incur provider charges.

<span id="with-the-template"></span>

## Create the host

Install the published template, then create a project. The template pins every
Tracon package to the same version as the template package. Pass the chat model
your account serves today with `--model`: Tracon ships no model list, so the name
comes from the provider's own documentation. The template writes it to
`appsettings.json`; you do not edit any code.

```bash
dotnet new install Tracon.Templates@1.0.0-preview.6
dotnet new tracon-api -n MyAgents --model gpt-5.4-mini
cd MyAgents
```

Use `--persistence`, `--provider`, and `--ui` to change the generated project.
Run `dotnet new tracon-api -h` to see the available values. The console is
embedded in the host; no separate console server is needed.

Store your provider key in the application's development secrets. The key never
enters the project; `appsettings.json` carries only an empty placeholder. The
project file already carries its secrets ID, so there is no `init` step.

```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "YOUR_API_KEY"
```

<span id="by-hand"></span>

## Choose the model

The generated `Program.cs` already registers an agent named `support` and one tool,
`get_order_status`, from `Tools/OrderTools.cs`. The agent runs with the model you
passed to `--model`. That name is a setting, not code: it sits in
`appsettings.json` next to the empty key placeholder, and `Program.cs` reads it.

```json
{
  "Tracon": {
    "Providers": {
      "OpenAI": { "ApiKey": "", "DefaultModel": "gpt-5.4-mini" }
    }
  }
}
```

To change the model later, change `DefaultModel` in `appsettings.json`, or
override it with a user secret or an environment variable. Azure OpenAI names a
deployment, so its setting is `DefaultDeployment`.

With an API key set and no model name, the host stops at startup and names
`Tracon:Providers:OpenAI:DefaultModel`, instead of failing on the first model
call. With no key at all, the host still starts, and only a run that calls the
model fails.

The agent definition in `Program.cs` binds that setting to the agent explicitly:

```csharp
tracon.AddAgent(new AgentDefinition
{
    Name = "support",
    DisplayName = "Support Assistant",
    Description = "Answers order and shipping questions.",
    Instructions = "You are a support assistant. Answer briefly and clearly. Always use a tool for order questions.",
    Model = new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = modelName, // Tracon:Providers:OpenAI:DefaultModel
    },
    ToolNames = ["get_order_status"],
});
```

This first agent uses a chat model only. To let a later agent generate stored image
attachments, add `UseOpenAIImages(...)`, set `Tracon:Images:Enabled`, and choose
an image model explicitly — one the provider serves for image generation, which is
not always the same family as its chat models. An image model is not inferred from
this agent's chat model; see
[image generation providers](/guides/model-providers/#image-generation-providers).

Start the host:

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

An agent defined in code with one tool, a console, and a recorded history — with no
database. With the default `--persistence memory`, every store is in memory, so all of
it ends when the process does.

## Read next

- [Adding a tool](/getting-started/tools/) — register a C# method the model can call.
- [Persistence](/getting-started/persistence/) — retain definitions and run records across restarts.
- [Securing the endpoints](/getting-started/security/) — configure authorization before exposing the host.
