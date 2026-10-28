# Tracon.Templates

A `dotnet new` template — generates a working [Tracon](https://www.nuget.org/packages/Tracon) control plane.

## Install

```bash
dotnet new install Tracon.Templates@1.0.0-preview.6
```

Check [nuget.org](https://www.nuget.org/packages/Tracon.Templates) before pinning a
newer preview.

## Usage

```bash
dotnet new tracon-api -n My.Agent --model <chat-model>
cd My.Agent
dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "sk-..."
dotnet run
```

`--model` is the chat model your provider serves today. Tracon ships no model list, so take the name from the provider's own documentation. The template writes it to `appsettings.json`; no code is edited.

A running control plane opens at `http://localhost:5081/tracon`.

## Options

| Option | Values | Default | What it does |
|---|---|---|---|
| `--persistence` | `memory`, `postgres`, `sqlite`, `sqlserver` | `memory` | Persistence provider |
| `--provider` | `openai`, `anthropic`, `google`, `azure` | `openai` | Model provider |
| `--ui` | `true`, `false` | `true` | Embedded control plane UI |
| `--model` | a model name | empty | The chat model of the sample agent, written to `appsettings.json` as the provider's `DefaultModel` (Azure OpenAI: `DefaultDeployment`) |

```bash
dotnet new tracon-api -n My.Agent --persistence postgres --provider anthropic --model <chat-model> --ui true
```

The generated project references every Tracon package at the template's own version
and treats NuGet warning `NU1608` as an error, so a reference that moves one Tracon
package to another version stops at restore. Delete that line from the project file to
keep `NU1608` a warning.

The generated `appsettings.json` carries empty placeholders for the `secret` values and the model name you passed — it never contains a `secret`. The connection string and API key are set with `dotnet user-secrets`; the generated `README.md` describes this as the first step. With an API key set and no model name, the app stops at startup and names the setting to fill in.

Details: <https://tracon.dev/getting-started/first-agent/>

Licence: MIT - the code this template generates is yours, under no obligation to
Tracon's terms. The packages it references carry their own licence; most are
PolyForm Small Business 1.0.0, which is free below 100 people and 1,000,000 USD (2019,
inflation adjusted) revenue. Details: <https://tracon.dev/reference/licensing/>
