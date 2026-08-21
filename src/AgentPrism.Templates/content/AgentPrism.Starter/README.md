# AgentPrism.Starter

Generated with `dotnet new agentprism-api`. This is a working [AgentPrism](https://agentprism.doayen.web.tr) control plane.

## 1. Set your secrets

The connection string and API key **never** enter this repository. `appsettings.json` carries only empty placeholders. Use `dotnet user-secrets`:

```bash
dotnet user-secrets init
```

**Persistence** — set only the *one* matching the `--persistence` value you chose at generation time:

```bash
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=...;Port=5432;Database=AgentPrism;Username=...;Password=..."
dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString"  "Server=...,1433;Database=AgentPrism;User Id=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "AgentPrism:Sqlite:ConnectionString"     "Data Source=agentprism.db"
```

If none is set, storage falls back to in-memory — nothing breaks, data just ends with the process.

**Model provider** — set only the *one* matching the `--provider` value you chose:

```bash
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey"        "sk-..."
dotnet user-secrets set "AgentPrism:Providers:Anthropic:ApiKey"     "sk-ant-..."
dotnet user-secrets set "AgentPrism:Providers:Google:ApiKey"        "AIza..."
dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:ApiKey"   "..."
```

If the API key is not set, the app still starts; only runs that actually call the model return an error.

## 2. Choose the model name

AgentPrism **carries no built-in model list** — model names and pricing change faster than any NuGet package's release cadence. Replace the `WRITE_MODEL_NAME_HERE` placeholder in [`Program.cs`](Program.cs) with the real model name from the provider's current documentation (for example, `gpt-5.4-mini` for OpenAI).

## 3. Run it

```bash
dotnet run
```

A running control plane opens at `http://localhost:5081/agentprism`. For the catalog, trial runs, and OpenAI-compatible endpoints:

```bash
curl http://localhost:5081/agentprism/api/agents
```

## Add a tool

[`Tools/OrderTools.cs`](Tools/OrderTools.cs) has a sample tool. To add a new tool, write a **static** method marked with `[AgentPrismTool]` and register it in `Program.cs` with `AddToolsFrom(typeof(...))`. Tools are defined only in code — this is a security boundary; the UI only lets users pick from registered tools.

## More

- [AgentPrism documentation](https://agentprism.doayen.web.tr)
- [Architecture](https://agentprism.doayen.web.tr/concepts/)
