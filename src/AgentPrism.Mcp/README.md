# AgentPrism.Mcp

Connects tools from remote **Model Context Protocol** servers into the AgentPrism catalog.

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString)
       .UseMcp();
```

Servers are defined not in code but in the **database**; they are added from
the UI or via the `PUT {prefix}/api/mcp-servers/{name}` endpoint. Discovery
happens in the background, and the tools found are listed alongside the
tools registered in code.

## Security boundary

Adding an MCP server means **accepting tool definitions from an external
source**, and is a deliberate exception to AgentPrism's "tools are defined
only in code" rule. It comes with the following safeguards:

| Safeguard | How |
|--------|-----|
| Remote server only | Only `http`/`https`. **No stdio** — starting a process on the server would mean anyone with UI access could run programs on the server |
| Approval required | MCP tools default to `RequiresApproval = true`; a call waits for user approval |
| No name hijacking | An MCP tool carrying the name of a tool already registered in code is **ignored**; code always wins |
| No secret leakage | The server record carries no authentication **value**, only the **name** of the configuration key the value is read from |
| Audit trail | Every call is written to the `tool_invocations` table with the source server name |
| Volume limit | An upper bound on tool count per server (`MaxToolsPerServer`, default 100) |

## Authentication

A secret is **never written** to the database. Only the key's name is stored
in the server record:

```json
{
  "endpoint": "https://mcp.example.com/mcp",
  "authorizationConfigurationKey": "AgentPrism:Mcp:ExampleToken"
}
```

The value is resolved at run time through `IConfiguration`:

```bash
dotnet user-secrets set "AgentPrism:Mcp:ExampleToken" "Bearer ..."
```

This way, the database backup, the audit trail, and the UI response never carry a secret.

## Tool names

Discovered tools are named in the form `{server}_{tool}`. The prefix is
mandatory: it is common for two different servers to have a tool with the
same name. A dot is **not used** — OpenAI and compatible providers accept
only `[a-zA-Z0-9_-]` in function names.

## Options

| Option | Default | What it does |
|------|-----------|----------|
| `Enabled` | `true` | Whether discovery is on |
| `RefreshInterval` | 5 min | How often the tool list is refreshed |
| `ConnectionTimeout` | 30 sec | The upper bound for connecting and listing |
| `MaxToolsPerServer` | 100 | The upper bound on tool count per server |

Refresh normally happens in the background. To see a newly added server's
tools right away, call `POST {prefix}/api/mcp-servers/refresh`.

## Behavior

- Being unable to **reach a server is not an error**: that server's tools
  drop out of the list, the others keep working, a warning is logged.
- The first discovery **does not block** application startup. An
  unreachable MCP server does not prevent the application from starting.
- Connections are **kept alive** between refreshes; they are only
  re-established when the server definition changes. Closing an unchanged
  connection would break a tool call in progress.
- Tools are resolved **per tenant**: a tool coming from one tenant's server
  is not visible to another tenant.

## AOT

This package is **not AOT-compatible**. MCP tool schemas are resolved at run
time, and `ModelContextProtocol.Core` uses reflection for JSON
serialization. `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, and
`.OpenAI` remain AOT-compatible.

Details: <https://farukatasoy.github.io/AgentPrism/concepts/tools/>
