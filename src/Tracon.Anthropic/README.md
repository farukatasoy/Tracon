# Tracon.Anthropic

Anthropic (Claude) provider adapter for Tracon.

```csharp
builder.AddTracon()
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

Every produced `IChatClient` goes through the same pipeline as `Tracon.OpenAI`:
`UseFunctionInvocation()` leaves the tool-call loop to the Microsoft Agent Framework,
`UseOpenTelemetry()` produces spans under the `Tracon` source. Circuit-breaker and
content-filter detection live at the `ModelProviderRegistry` level; this package gets
both without extra code.

## SDK used

The official [`Anthropic`](https://www.nuget.org/packages/Anthropic) package (owner:
Anthropic, MIT). The package carries its own `AsIChatClient` adapter, so message
mapping, streaming, tool calling and usage counters are not written in Tracon.
Its transitive dependencies are only `Microsoft.Extensions.AI.Abstractions`,
`System.Net.ServerSentEvents`, `System.Text.Json` and `System.IO.Pipelines`.

**Not Native AOT-compatible.** The package's own code is analyzer-clean
(`IsAotCompatible=true`, zero warnings), but the Anthropic SDK's `IChatClient`
adapter initializes reflection-based JSON serialization: a Native AOT app that
calls the provider fails on the first request with `Reflection-based serialization
has been disabled`, and the publish emits thousands of trim and AOT warnings from
the SDK. Measured 2026-10-17 with SDK 12.58.0.

## `max_tokens` is required

In the Anthropic Messages API, `max_tokens` is a **required** field; it cannot
be omitted the way it can in OpenAI. When `ModelBinding.MaxOutputTokens` is left
empty, `AnthropicProviderOptions.DefaultMaxOutputTokens` (default **4096**) is used.

A response that reaches the limit stops in the middle. A structured (`Json` or
`JsonSchema`) response then fails the run as `output_truncated`; a text answer
completes with a `tracon.output.truncated` run event. Thinking tokens count
against the same limit, so set `MaxOutputTokens` explicitly for long answers and at
high effort. When the catalog states the model's `MaxOutputTokens`,
`POST /api/agents/validate` warns about a structured or high-effort agent left on
the default.

A response the model stopped because the conversation filled its context window
(`model_context_window_exceeded`) is a cut-off answer too. The error message and
the event's `cause` (`context_window`) name the window: shorten the conversation or
use a model with a larger window; a larger `MaxOutputTokens` does not help.

A turn the API paused (`pause_turn`, a long turn of its own server tools such as web
search) is continued: the paused answer goes back as the API returned it, server
tool blocks included, and the model goes on. Each call is billed and counted. A turn
still paused after five continuations fails the call.

## Provider-specific settings

Given per agent through the `ModelBinding.ProviderSettings` dictionary. **A key
absent from this list is not silently ignored** — it fails compilation and the
message lists the supported keys.

| Key | Type | Default | Description |
|---|---|---|---|
| `anthropic.effort` | text | none (model default) | `output_config.effort`: `low`, `medium`, `high`, `xhigh` or `max`, read from the SDK's `Effort` enum |
| `anthropic.thinking.mode` | text | none | `adaptive` sends `thinking: {"type":"adaptive"}`; `enabled` requires a budget |
| `anthropic.thinking.budgetTokens` | integer | none | Extended thinking budget; on its own it means `enabled` |
| `anthropic.promptCaching` | boolean | `false` | Turns prompt caching on |
| `anthropic.promptCaching.ttl` | text | none (5 minutes) | `cache_control.ttl`: `5m` or `1h` |
| `anthropic.promptCaching.placement` | text | `request` | `request` adds a top-level `cache_control`; `system` marks the end of the agent's instructions instead, and a request with no instructions gets the `request` breakpoint |

A wrong value is an error that names the key and the valid values. `ttl` and
`placement` without `anthropic.promptCaching = true`, `enabled` without a budget,
and `adaptive` with a budget are errors too.

```csharp
var binding = new ModelBinding
{
    Provider = AnthropicProviderNames.Anthropic,
    Model = "your-current-claude-model",
    MaxOutputTokens = 64000,
    ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
    {
        [AnthropicProviderNames.EffortSetting] = JsonSerializer.SerializeToElement("high"),
        [AnthropicProviderNames.ThinkingModeSetting] = JsonSerializer.SerializeToElement("adaptive"),
        [AnthropicProviderNames.PromptCachingSetting] = JsonSerializer.SerializeToElement(true),
        [AnthropicProviderNames.PromptCachingTtlSetting] = JsonSerializer.SerializeToElement("1h"),
        [AnthropicProviderNames.PromptCachingPlacementSetting] = JsonSerializer.SerializeToElement("system"),
    },
};
```

### Known behavior differences

- **Current models reject a thinking budget and sampling parameters** with `400`.
  Use `anthropic.thinking.mode = adaptive` with `anthropic.effort`, leave
  `Temperature` and `TopP` empty, and set `SupportsSampling = false` on the model's
  catalog entry so such a definition fails at compile time instead of at run time.
- **`ModelBinding.ReasoningEffort` becomes adaptive thinking.** It is sent as
  `thinking: {"type":"adaptive"}` plus `output_config.effort` (`low`, `medium`, `high`,
  `xhigh`), which every current Claude model accepts. A model that predates adaptive
  thinking (`claude-haiku-4-5`) rejects it with `400`: select that model with
  `anthropic.thinking.mode = enabled` and `anthropic.thinking.budgetTokens`. A `thinking`
  provider setting wins over `ReasoningEffort`. Measured live on 2026-10-17 across the
  Claude 5 family and `claude-haiku-4-5`.
- **Temperature must be 1 while budgeted thinking is on.** When
  `anthropic.thinking.budgetTokens` is given, `ModelBinding.Temperature` must be
  either left empty or set to `1`; any other value gets the request rejected with
  `invalid_request_error`.
- **The thinking budget must be smaller than `MaxOutputTokens`.**
- **Prompt caching requires a threshold.** Short prompts are not cached. The
  `request` placement treats the whole request as one cache unit, so a changed
  document or question writes a new entry. The `system` placement caches only up
  to the end of the instructions, which requests with different documents share.
  The effect is visible in the cached-input token counters of the run's usage.
- **Provider settings do not reach a fallback link.** A `Fallbacks` link gets the
  binding's portable fields only.
- **The system message is a separate field.** Anthropic carries the `system`
  field outside the message list; the SDK adapter does the conversion, Tracon
  has no work to do here.

## Health check

Calls `GET {endpoint}/models`, **incurs no cost**. Authentication is done through
the `x-api-key` header and the `anthropic-version: 2023-06-01` header is required.
The error detail is limited to the HTTP status code and a short reason; the API
key or the endpoint address **never leaks**.

```
GET /tracon/api/models/health/anthropic
```

## Model catalog

Tracon carries no built-in model list: model names change faster than a
NuGet release. The catalog comes
entirely from configuration and is **not a validation list** — a model name
absent from it can still be used.

```json
{
  "Tracon": {
    "Providers": {
      "Anthropic": {
        "ApiKey": "",
        "DefaultModel": "claude-sonnet-5",
        "DefaultMaxOutputTokens": 4096,
        "Models": [
          { "Name": "claude-sonnet-5", "DisplayName": "Claude Sonnet 5",
            "ContextWindowTokens": 200000, "MaxOutputTokens": 128000,
            "SupportsReasoning": true, "SupportsSampling": false,
            "InputCostPerMillionTokens": 3, "OutputCostPerMillionTokens": 15 }
        ]
      }
    }
  }
}
```

`ApiKey` is **never** written to this file — use `dotnet user-secrets`.

## Links

- Guide: <https://tracon.dev/guides/model-providers/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
