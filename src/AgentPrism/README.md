# AgentPrism

An agent control plane built on the Microsoft Agent Framework: define agents, run
them, and see what they did.

This is the meta package. It is a single reference that brings the common set —
runtime, PostgreSQL persistence, the OpenAI provider, the HTTP API, workflows, MCP,
and the embedded management UI.

```bash
dotnet add package AgentPrism
```

```csharp
builder.AddAgentPrism()
       .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!)
       .UsePostgreSql(builder.Configuration.GetConnectionString("AgentPrism")!)
       .AddTool(GetOrderStatus)
       .UseUI();

var app = builder.Build();

app.MapAgentPrism("/agentprism");
app.Run();
```

Open `/agentprism` and the console is there: create an agent, run it in the
playground, then read the recorded run event by event.

Or start from the template, which writes a working application for you:

```bash
dotnet new install AgentPrism.Templates
dotnet new agentprism-api -o MyAgents
```

## What comes with it

| Package | What it does |
|---|---|
| `AgentPrism.Abstractions` | The contracts everything is written against |
| `AgentPrism.Core` | Runtime: catalog, definition compiler, tool registry, run recording |
| `AgentPrism.PostgreSql` | Persistence, and the vector store behind knowledge search |
| `AgentPrism.OpenAI` | The OpenAI provider |
| `AgentPrism.AspNetCore` | The HTTP API — 143 operations, layered access control |
| `AgentPrism.Workflows` | Multi-agent workflows, checkpoints, human-in-the-loop |
| `AgentPrism.Mcp` | Tools from remote MCP servers |
| `AgentPrism.UI` | The embedded management console — 30 screens, no `node_modules` |

## Install pieces instead

Every one of these also stands alone. Take `AgentPrism.Core` plus a provider if you
want the runtime and nothing else; swap `AgentPrism.SqlServer` or `AgentPrism.Sqlite`
in for PostgreSQL; add `AgentPrism.Anthropic`, `.Google`, `.Azure`, or `.Voice` as you
need them. `AgentPrism.Testing` carries the fakes for your own tests.

Reach for the meta package when you want the usual set; reach for the individual ones
when you care about what enters your dependency graph.

## Two things worth knowing early

**Tools are defined in code only.** An agent can be created and edited from the UI or
the API, but tool code can never be written through them. That is a security
boundary, not a limitation to be configured away.

**Nothing is exposed by default.** `MapAgentPrism` restricts the endpoints to
loopback until you turn remote access on, and secrets are never written to the
database — a record stores the *name* of the configuration key a value is read from,
never the value.

## Status

Pre-release. The Microsoft Agent Framework packages this builds on are themselves in
preview, and the public API is still moving toward `1.0`.

## Links

- Full documentation: <https://agentprism.doayen.web.tr>
- Getting started: <https://agentprism.doayen.web.tr/>
- API reference: <https://agentprism.doayen.web.tr/api/>

License: MIT
