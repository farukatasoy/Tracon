# AgentPrism.AspNetCore

AgentPrism HTTP layer.

`MapAgentPrism` connects two endpoint groups:

- **Management API** — agent CRUD, sessions, runs, tools, models
- **OpenAI-compatible endpoints** — `/v1/responses`, `/v1/conversations`, `/v1/chat/completions`

Includes layered access protection: loopback only by default, an optional bearer token, and an ASP.NET Core authorization policy hook.

```csharp
app.MapAgentPrism("/agentprism", options =>
{
    options.RequireAuthorization("AgentPrismAdmin");
});
```

> **Pre-release note:** This package depends on `Microsoft.Agents.AI.Hosting` (preview) and `Microsoft.Agents.AI.Hosting.OpenAI` (alpha). All of AgentPrism's pre-release dependencies are deliberately consolidated into this single package.

## Installation

```bash
dotnet add package AgentPrism.AspNetCore
```

## Links

- Repository and full documentation: <https://github.com/farukatasoy/AgentPrism>
- Architecture: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

License: MIT
