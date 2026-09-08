# AgentPrism.Google

Google Gemini provider adapter for AgentPrism.

## Image generation

After `UseGoogle(...)`, call `UseGoogleImages(...)` to register AgentPrism's narrow
`IImageGenerator` adapter. Set an image-capable Google model explicitly.

```csharp
agentPrism.UseGoogleImages(options =>
{
    options.Enabled = true;
    options.Model = "your-imagen-model";
});
```

The generator key is `google`. This adapter generates from a prompt only; it does not
map a `WIDTHxHEIGHT` request because Google's image API uses separate aspect-ratio and
size-tier settings. Configure prices under `AgentPrism:Pricing:Images:google` when
cost reporting is required.

```csharp
builder.AddAgentPrism()
       .UseGoogle(apiKey, o => o.DefaultModel = "gemini-3.6-flash");
```

Registered provider name: **`google`** (`GoogleProviderNames.Google`).

```csharp
Model = new ModelBinding
{
    Provider = GoogleProviderNames.Google,
    Model = "gemini-3.6-flash",
}
```

The name is `google`, not `gemini`: the same package may cover Vertex AI in the
future and should not be locked to the model family's name.

Every `IChatClient` produced goes through the same pipeline as `AgentPrism.OpenAI`:
`UseFunctionInvocation()` leaves the tool-call loop to the Microsoft Agent Framework,
`UseOpenTelemetry()` produces spans under the `AgentPrism` source. Circuit breaker
and content filter detection live at the `ModelProviderRegistry` level.

## SDK used and dependency weight

The official [`Google.GenAI`](https://www.nuget.org/packages/Google.GenAI) package
(owner: Google LLC, Apache-2.0). The package carries its own `AsIChatClient` adapter.

**This package is heavier than the others.** `Newtonsoft.Json`, `System.Management`,
and `System.CodeDom` come in transitively via `Google.Apis.Auth`. The weight is a
deliberate trade-off and is kept isolated inside this package: a consumer not using
Gemini pulls in none of it. Other AgentPrism packages are unaffected by these
dependencies.

The package is AOT-compatible (`IsAotCompatible=true`, zero warnings).

## Provider-specific settings

Supplied per agent via the `ModelBinding.ProviderSettings` dictionary. **A key not
in this list is not silently ignored** — it produces a build error, and the message
lists the supported keys.

| Key | Type | Description |
|---|---|---|
| `google.safety.harassment` | string | Harassment threshold |
| `google.safety.hateSpeech` | string | Hate speech threshold |
| `google.safety.sexuallyExplicit` | string | Sexual content threshold |
| `google.safety.dangerousContent` | string | Dangerous content threshold |
| `google.safety.civicIntegrity` | string | Civic integrity threshold |
| `google.thinking.budgetTokens` | integer | Thinking budget, `[-1, 65535]` |
| `google.thinking.includeThoughts` | boolean | Whether a thinking summary is returned in the response |

Valid threshold values: `BLOCK_LOW_AND_ABOVE`, `BLOCK_MEDIUM_AND_ABOVE`,
`BLOCK_ONLY_HIGH`, `BLOCK_NONE`, `OFF`. An unrecognized value produces a build error
and lists the valid values.

```csharp
ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
{
    ["google.safety.harassment"] = JsonSerializer.SerializeToElement("BLOCK_ONLY_HIGH"),
    ["google.thinking.budgetTokens"] = JsonSerializer.SerializeToElement(512),
}
```

## Safety filter produces an empty response

When Gemini's safety filter kicks in, the response comes back **empty** and the
finish reason is `content_filter`. AgentPrism does not treat this as "succeeded but
empty": the run is recorded with `Failed` status and `RunError.Type =
"content_filtered"`. A silent empty response is the hardest kind of failure to
debug.

Detection lives in a shared decorator inside `AgentPrism.Core`; if the model produced
text before being cut off (a partial response), no error is thrown — there is a
usable answer in hand.

## Known behavioral differences

- **Model names go stale quickly.** Measured: calling
  `gemini-2.5-flash` returned *"This model is no longer available to new users"*.
  The catalog comes from configuration and is not a validation
  list.
- **The model list carries a resource path.** The health endpoint returns
  `models/gemini-3.6-flash` rather than `gemini-3.6-flash`; the prefix is stripped.
- **The system message is a separate field** (`systemInstruction`); the SDK handles
  the conversion.
- **Usage counters arrive at the end when streaming.**

## Health check

Hits `GET {endpoint}/{apiVersion}/models`, which **produces no charge**.
Authentication is done with the `x-goog-api-key` header — the key is deliberately
not put in the query string, because query strings get written in plain text to
proxy and access logs.

```
GET /agentprism/api/models/health/google
```

## Model catalog

```json
{
  "AgentPrism": {
    "Providers": {
      "Google": {
        "ApiKey": "",
        "DefaultModel": "gemini-3.6-flash",
        "Models": [
          { "Name": "gemini-3.6-flash", "DisplayName": "Gemini 3.6 Flash",
            "ContextWindowTokens": 1048576, "MaxOutputTokens": 65536,
            "SupportsReasoning": true }
        ]
      }
    }
  }
}
```

`ApiKey` is **never** written to this file — use `dotnet user-secrets`.

## Links

- Guide: <https://agentprism.doayen.web.tr/guides/model-providers/>
- Capability map: <https://agentprism.doayen.web.tr/capabilities/>
- API reference: <https://agentprism.doayen.web.tr/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://agentprism.doayen.web.tr/reference/licensing/>
