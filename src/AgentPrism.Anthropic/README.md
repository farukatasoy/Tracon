# AgentPrism.Anthropic

Anthropic (Claude) provider adapter for AgentPrism.

```csharp
builder.AddAgentPrism()
       .UseAnthropic(apiKey, o =>
       {
           o.DefaultModel = "claude-sonnet-5";
           o.DefaultMaxOutputTokens = 4096;
       });
```

Registered provider name: **`anthropic`** (`AnthropicProviderNames.Anthropic`).

```csharp
Model = new ModelBinding
{
    Provider = AnthropicProviderNames.Anthropic,
    Model = "claude-sonnet-5",
    MaxOutputTokens = 1024,
}
```

Every produced `IChatClient` goes through the same pipeline as `AgentPrism.OpenAI`:
`UseFunctionInvocation()` leaves the tool-call loop to the Microsoft Agent Framework,
`UseOpenTelemetry()` produces spans under the `AgentPrism` source. Circuit-breaker and
content-filter detection live at the `ModelProviderRegistry` level; this package gets
both without extra code.

## SDK used

The official [`Anthropic`](https://www.nuget.org/packages/Anthropic) package (owner:
Anthropic, MIT). The package carries its own `AsIChatClient` adapter, so message
mapping, streaming, tool calling and usage counters are not written in AgentPrism.
Its transitive dependencies are only `Microsoft.Extensions.AI.Abstractions`,
`System.Net.ServerSentEvents`, `System.Text.Json` and `System.IO.Pipelines`.

The package is AOT compatible (`IsAotCompatible=true`, zero warnings).

## `max_tokens` is required

In the Anthropic Messages API, `max_tokens` is a **required** field; it cannot
be omitted the way it can in OpenAI. When `ModelBinding.MaxOutputTokens` is left
empty, `AnthropicProviderOptions.DefaultMaxOutputTokens` (default **4096**) is used.

## Provider-specific settings

Given per agent through the `ModelBinding.ProviderSettings` dictionary. **A key
absent from this list is not silently ignored** — it fails compilation and the
message lists the supported keys.

| Key | Type | Default | Description |
|---|---|---|---|
| `anthropic.promptCaching` | boolean | `false` | Adds `cache_control: {"type":"ephemeral"}` to the request |
| `anthropic.thinking.budgetTokens` | integer | none | Extended thinking budget |

```csharp
Model = new ModelBinding
{
    Provider = AnthropicProviderNames.Anthropic,
    Model = "claude-sonnet-5",
    MaxOutputTokens = 4096,
    ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic.thinking.budgetTokens"] = JsonSerializer.SerializeToElement(2048),
    },
}
```

### Known behavior differences

- **Temperature must be 1 while thinking is on.** When `anthropic.thinking.budgetTokens`
  is given, `ModelBinding.Temperature` must be either left empty or set to `1`;
  any other value gets the request rejected with `invalid_request_error`.
- **The thinking budget must be smaller than `MaxOutputTokens`.**
- **Prompt caching requires a threshold.** Short prompts are not cached; the
  top-level `cache_control` treats the entire request as a single cache unit, so a
  changed user message re-creates the cache. The effect is visible in the
  `CacheCreationInputTokens` / `CacheReadInputTokens` counters on the response.
- **The system message is a separate field.** Anthropic carries the `system`
  field outside the message list; the SDK adapter does the conversion, AgentPrism
  has no work to do here.

## Health check

Calls `GET {endpoint}/models`, **incurs no cost**. Authentication is done through
the `x-api-key` header and the `anthropic-version: 2023-06-01` header is required.
The error detail is limited to the HTTP status code and a short reason; the API
key or the endpoint address **never leaks**.

```
GET /agentprism/api/models/health/anthropic
```

## Model catalog

AgentPrism carries no built-in model list: model names change faster than a
NuGet release. The catalog comes
entirely from configuration and is **not a validation list** — a model name
absent from it can still be used.

```json
{
  "AgentPrism": {
    "Providers": {
      "Anthropic": {
        "ApiKey": "",
        "DefaultModel": "claude-sonnet-5",
        "DefaultMaxOutputTokens": 4096,
        "Models": [
          { "Name": "claude-sonnet-5", "DisplayName": "Claude Sonnet 5",
            "ContextWindowTokens": 200000, "SupportsReasoning": true,
            "InputCostPerMillionTokens": 3, "OutputCostPerMillionTokens": 15 }
        ]
      }
    }
  }
}
```

`ApiKey` is **never** written to this file — use `dotnet user-secrets`.

## Links

- Guide: <https://farukatasoy.github.io/AgentPrism/guides/model-providers/>
- Capability map: <https://farukatasoy.github.io/AgentPrism/capabilities/>
- API reference: <https://farukatasoy.github.io/AgentPrism/api/>
