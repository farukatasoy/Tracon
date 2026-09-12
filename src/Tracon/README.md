# Tracon

An agent control plane built on the Microsoft Agent Framework: define agents, run
them, and see what they did.

This is the meta package. It is a single reference that brings the common set —
runtime, PostgreSQL persistence, the OpenAI provider, the HTTP API, workflows, MCP,
and the embedded management UI.

```bash
dotnet add package Tracon
```

```csharp
builder.AddTracon()
       .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!)
       .UsePostgreSql(builder.Configuration.GetConnectionString("Tracon")!)
       .AddTool(GetOrderStatus)
       .UseUI();

var app = builder.Build();

app.MapTracon("/tracon");
app.Run();
```

Open `/tracon` and the console is there: create an agent, run it in the
playground, then read the recorded run event by event.

Or start from the template, which writes a working application for you:

```bash
dotnet new install Tracon.Templates
dotnet new tracon-api -o MyAgents
```

## What comes with it

| Package | What it does |
|---|---|
| `Tracon.Abstractions` | The contracts everything is written against |
| `Tracon.Core` | Runtime: catalog, definition compiler, tool registry, run recording |
| `Tracon.PostgreSql` | Persistence, and the vector store behind knowledge search |
| `Tracon.OpenAI` | The OpenAI provider |
| `Tracon.AspNetCore` | The HTTP API — 143 operations, layered access control |
| `Tracon.Workflows` | Multi-agent workflows, checkpoints, human-in-the-loop |
| `Tracon.Mcp` | Tools from remote MCP servers |
| `Tracon.UI` | The embedded management console — 30 screens, no `node_modules` |

## Install pieces instead

Every one of these also stands alone. Take `Tracon.Core` plus a provider if you
want the runtime and nothing else; swap `Tracon.SqlServer` or `Tracon.Sqlite`
in for PostgreSQL; add `Tracon.Anthropic`, `.Google`, `.Azure`, or `.Voice` as you
need them. `Tracon.Testing` carries the fakes for your own tests.

Reach for the meta package when you want the usual set; reach for the individual ones
when you care about what enters your dependency graph.

## Two things worth knowing early

**Tools are defined in code only.** An agent can be created and edited from the UI or
the API, but tool code can never be written through them. That is a security
boundary, not a limitation to be configured away.

**Nothing is exposed by default.** `MapTracon` restricts the endpoints to
loopback until you turn remote access on, and secrets are never written to the
database — a record stores the *name* of the configuration key a value is read from,
never the value.

## Status

Pre-release. The Microsoft Agent Framework packages this builds on are themselves in
preview, and the public API is still moving toward `1.0`.

## Links

- Full documentation: <https://tracon.dev>
- Getting started: <https://tracon.dev/>
- API reference: <https://tracon.dev/api/>

License: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
