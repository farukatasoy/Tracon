# Tracon.Starter

Generated with `dotnet new tracon-api`. This is a working [Tracon](https://tracon.dev) control plane.

## 1. Set your secrets

The connection string and API key **never** enter this repository. `appsettings.json` carries only empty placeholders. Use `dotnet user-secrets`. The project file already carries its `UserSecretsId`, so there is no `init` step.

**Persistence** — set only the *one* matching the `--persistence` value you chose at generation time:

```bash
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" "Host=...;Port=5432;Database=Tracon;Username=...;Password=..."
dotnet user-secrets set "Tracon:SqlServer:ConnectionString"  "Server=...,1433;Database=Tracon;User Id=...;Password=...;TrustServerCertificate=true"
dotnet user-secrets set "Tracon:Sqlite:ConnectionString"     "Data Source=tracon.db"
```

If none is set, storage falls back to in-memory — nothing breaks, data just ends with the process.

**Model provider** — set only the *one* matching the `--provider` value you chose:

```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey"        "sk-..."
dotnet user-secrets set "Tracon:Providers:Anthropic:ApiKey"     "sk-ant-..."
dotnet user-secrets set "Tracon:Providers:Google:ApiKey"        "AIza..."
dotnet user-secrets set "Tracon:Providers:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets set "Tracon:Providers:AzureOpenAI:ApiKey"   "..."
```

If the API key is not set, the app still starts; only runs that actually call the model return an error.

## 2. Choose the model name

Tracon **carries no built-in model list** — model names and pricing change faster than any NuGet package's release cadence. Take the model name from the provider's current documentation (for example, `gpt-5.4-mini` for OpenAI).

If you generated the project with `--model`, there is nothing to do: `dotnet new tracon-api --model <name>` already wrote the name to `appsettings.json` as the provider's `DefaultModel` (Azure OpenAI: `DefaultDeployment`). If you did not, fill that setting in — it is not a secret, so `appsettings.json` is the right place. `Program.cs` needs no edit.

With an API key set and no model name, the app stops at startup and names the setting, instead of failing on the first model call.

## 3. Run it

```bash
dotnet run
```

A running control plane opens at `http://localhost:5081/tracon`. For the catalog, trial runs, and OpenAI-compatible endpoints:

```bash
curl http://localhost:5081/tracon/api/agents
```

## Add a tool

[`Tools/OrderTools.cs`](Tools/OrderTools.cs) has a sample tool. To add a new tool, write a **static** method marked with `[TraconTool]`; the `AddGeneratedTools()` call in `Program.cs` registers it at compile time, without reflection. Tools are defined only in code — this is a security boundary; the UI only lets users pick from registered tools.

## More

- [Tracon documentation](https://tracon.dev)
- [Architecture](https://tracon.dev/concepts/)
