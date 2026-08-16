---
title: Connect and expose agents
description: Consume MCP tools and context, or publish selected AgentPrism agents through MCP and A2A with explicit security boundaries.
---

AgentPrism supports three different external-agent directions. Keep them separate:

| Direction | Purpose | Registration | HTTP surface |
|---|---|---|---|
| MCP client | Bring remote tools, prompts, and resources into AgentPrism | `UseMcp()` | Managed through `/api/mcp-servers/*` |
| MCP server | Publish an AgentPrism agent as an MCP tool | `UseMcpServer()` | `/agentprism/mcp` by default |
| A2A server | Publish an agent through the agent-to-agent protocol | `UseA2A()` | `/agentprism/a2a/{agent}` by default |

The first direction expands what your agents can call. The other two expand who can
call your agents. They have different trust boundaries and must be enabled separately.

## Consume a remote MCP server

Add the non-AOT `AgentPrism.Mcp` package and register discovery:

```csharp
var agentPrism = builder.AddAgentPrism()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(connectionString)
    .UseMcp(options =>
    {
        options.RefreshInterval = TimeSpan.FromMinutes(5);
        options.ConnectionTimeout = TimeSpan.FromSeconds(30);
        options.MaxToolsPerServer = 100;
    });
```

Create server records through the console or management API. Only the configuration
key name is persisted; the credential value stays in your secret provider:

```json
{
  "endpoint": "https://mcp.example.com/mcp",
  "authorizationConfigurationKey": "AgentPrism:Mcp:ExampleToken"
}
```

```bash
dotnet user-secrets set "AgentPrism:Mcp:ExampleToken" "Bearer ..."
```

Discovery is asynchronous and does not block startup. Refresh immediately after a
configuration change with `POST {prefix}/api/mcp-servers/refresh`. An unreachable
server loses its discovered tools and produces a warning; other servers continue.

### The MCP client security boundary

- Only HTTP and HTTPS transports are accepted. AgentPrism does not start MCP `stdio`
  processes on the host.
- Discovered tool names are `{server}_{tool}`. A code-defined tool with the same name
  wins, so a remote server cannot replace it.
- New MCP tools require approval by default.
- Discovery is tenant-scoped and limited to 100 tools per server by default.
- OAuth tokens live in memory. Plan for reauthorization after a process restart.

An agent can also receive static MCP resources at run start through
`AgentDefinition.McpResourceUris`. Each entry is `{server}:{uri}`. The limits are
64 KB per resource and 256 KB total. `UseMcp()` is required. This field is currently
code/HTTP-only; the console editor does not preserve it.

## Publish agents as MCP tools

Choose the allowlist before the application is built, then map the management API
before the MCP endpoint:

```csharp
var agentPrism = builder.AddAgentPrism()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(connectionString)
    .UseMcpServer(options =>
    {
        options.ExposedAgents.Add("support");
        options.ToolNamePrefix = "acme";
        options.Budget = new AgentRunBudget
        {
            MaxDepth = 1,
            MaxTotalRuns = 4,
            MaxTotalTokens = 40_000,
        };
    });

var app = builder.Build();

app.MapAgentPrism("/agentprism", options =>
{
    options.AllowRemoteAccess = true;
    options.RequireRolePolicies = true;
});
app.MapAgentPrismMcpServer();
```

No agent is exposed by default. `ExposeAllAgents` exists, but an allowlist is safer
for a catalog that operators can edit. Each MCP call creates a fresh run budget; the
default maximum depth is one, so an external caller cannot open an unbounded agent
tree.

The endpoint inherits the loopback, bearer, and authorization settings from
`MapAgentPrism`. Remote exposure also requires at least one active, unexpired API key
with the exact `ExternalInvoke` scope. A static bearer token alone is rejected at
startup. Create the key before enabling remote access.

An exposed agent cannot contain an approval-required tool. The application waits
until the catalog is queryable, checks the selected agents, and prevents MCP requests
from running if an approval boundary would be crossed. An external protocol caller
cannot act as the missing human.

## Publish agents through A2A

A2A exposes a distinct identity and agent card for each selected agent:

```csharp
var agentPrism = builder.AddAgentPrism()
    .UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))
    .UsePostgreSql(connectionString)
    .UseA2A(options =>
    {
        options.ExposedAgents.Add("support");
        options.Budget = new AgentRunBudget
        {
            MaxDepth = 1,
            MaxTotalTokens = 40_000,
        };
    });

var app = builder.Build();

app.MapAgentPrism("/agentprism", options =>
{
    options.AllowRemoteAccess = true;
    options.RequireRolePolicies = true;
});
app.MapAgentPrismA2A();
```

The invocation URL is `/agentprism/a2a/support`; its card is under
`/agentprism/a2a/support/.well-known/agent-card.json`.

A2A names are frozen during service registration because the underlying hosting API
registers one server per name. The agent implementation is still resolved from the
catalog on every call, so updating a database definition changes later behavior, but
adding a new name requires an application restart and registration change. There is
no expose-all switch.

AgentPrism declares streaming, push notifications, and background A2A runs as
unsupported. The same `ExternalInvoke`, approval, tenant, and budget boundaries as
the MCP server apply.

## Production checklist

- [ ] Expose only names whose input contract is safe for another system.
- [ ] Create a tenant-bound `ExternalInvoke` key and test revocation and expiry.
- [ ] Keep approval-required and high-impact tools out of exposed definitions.
- [ ] Set token, depth, and child-run budgets for externally initiated work.
- [ ] Terminate HTTPS at a trusted proxy and preserve the request path.
- [ ] Alert on external run error rate, token use, latency, and rejected credentials.
- [ ] Test catalog availability when migrations are applied outside the process.

MCP server and A2A routes are protocol surfaces, not management endpoints. They do
not appear in the generated OpenAPI operation count. Their runs still use the normal
recording, tenancy, trace, cost, quota, and audit infrastructure.

## Read next

- [Tools, skills, and MCP](/AgentPrism/concepts/tools/)
- [Securing the endpoints](/AgentPrism/getting-started/security/)
- [Reliable runs](/AgentPrism/guides/reliability/)
- [Compatibility matrix](/AgentPrism/reference/compatibility/)
