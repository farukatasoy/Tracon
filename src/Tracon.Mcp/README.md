# Tracon.Mcp

Connects tools from remote **Model Context Protocol** servers into the Tracon catalog.

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .UsePostgreSql(connectionString)
       .UseMcp();
```

`UsePostgreSql` comes from the `Tracon.PostgreSql` package; add it with `dotnet add package Tracon.PostgreSql --prerelease`. Without a storage package every store is in memory.

Servers are defined not in code but in the **database**; they are added from
the UI or via the `PUT {prefix}/api/mcp-servers/{name}` endpoint. Discovery
happens in the background, and the tools found are listed alongside the
tools registered in code.

## Security boundary

Adding an MCP server means **accepting tool definitions from an external
source**, and is a deliberate exception to Tracon's "tools are defined
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

A secret is **never written** to the database. A credential header is declared
by the **name** of the configuration key its value is read from:

```json
{
  "endpoint": "https://mcp.example.com/mcp",
  "headerConfigurationKeys": {
    "Authorization": "Tracon:McpSecrets:ExampleToken",
    "X-Api-Key": "Tracon:McpSecrets:ExampleKey"
  }
}
```

The value is resolved on every connection through `IConfiguration`:

```bash
dotnet user-secrets set "Tracon:McpSecrets:ExampleToken" "Bearer ..."
dotnet user-secrets set "Tracon:McpSecrets:ExampleKey" "..."
```

Every key name must start with `Tracon:McpSecrets:` (the
`AllowedConfigurationPrefix`). In a multi-tenant installation a tenant other
than the default one names keys under `Tracon:McpSecrets:<tenant>:`.

Plain `headers` are stored as sent, so a save rejects a header whose name
looks like a credential (`Authorization`, `X-Api-Key`, `Cookie`, or any name
ending in `-key`) with `400`. The `Authorization` header is declared the same way,
as `headerConfigurationKeys["Authorization"]`; the older `authorizationConfigurationKey`
field is removed, and a save that still sends it returns `400`.

This way, the database backup, the audit trail, and the UI response never carry a secret.

## Tool names

Discovered tools are named in the form `{server}_{tool}`. The prefix is
mandatory: it is common for two different servers to have a tool with the
same name. A dot is **not used** — OpenAI and compatible providers accept
only `[a-zA-Z0-9_-]` in function names.

## When a server asks the user for input

A server can stop a tool call to ask the user for a value or a confirmation
(*elicitation*). Tracon declares that it can answer, with form fields only.

| Where the call runs | What the server gets |
|---|---|
| `POST {prefix}/api/agents/{name}/run` with a `sessionId` | The run closes `AwaitingInput` and the response carries the request (an `inputs` event). Send the answer as `inputResponses` in the next request of the session; the tool call then runs **again from its start** with the answer |
| A run without a `sessionId`, an OpenAI-compatible call, an A2A task, an MCP tool call, a queued run | `decline` |
| An agent called by another agent | `decline` |

A declined request inside a run writes a `tracon.tool.input.declined` run event with the reason (outside any run, it is only logged). The
answer is audited by the names of its fields, never by a value, and the values go to the
server and nowhere else. A tool call stops for input at most once; if the server asks
something else the second time, it gets `decline`.

Details: <https://tracon.dev/concepts/tools/#a-server-asks-the-user-for-input>

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
serialization. `Tracon.Abstractions`, `.Core`, `.PostgreSql`, and
`.OpenAI` remain AOT-compatible.

Details: <https://tracon.dev/concepts/tools/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
