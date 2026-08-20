# AgentPrism.Templates

A `dotnet new` template — generates a working [AgentPrism](https://www.nuget.org/packages/AgentPrism) control plane.

## Install

```bash
dotnet new install AgentPrism.Templates
```

## Usage

```bash
dotnet new agentprism-api -n My.Agent
cd My.Agent
dotnet user-secrets init
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
dotnet run
```

A running control plane opens at `http://localhost:5081/agentprism`.

## Options

| Option | Values | Default | What it does |
|---|---|---|---|
| `--persistence` | `memory`, `postgres`, `sqlite`, `sqlserver` | `memory` | Persistence provider |
| `--provider` | `openai`, `anthropic`, `google`, `azure` | `openai` | Model provider |
| `--ui` | `true`, `false` | `true` | Embedded control plane UI |

```bash
dotnet new agentprism-api -n My.Agent --persistence postgres --provider anthropic --ui true
```

The generated `appsettings.json` carries only empty placeholders — it never contains a `secret`. The connection string and API key are set with `dotnet user-secrets`; the generated `README.md` describes this as the first step.

Details: <https://farukatasoy.github.io/AgentPrism/getting-started/first-agent/>
