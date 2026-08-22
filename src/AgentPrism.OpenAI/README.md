# AgentPrism.OpenAI

The OpenAI provider adapter for AgentPrism.

## Image generation

After `UseOpenAI(...)`, call `UseOpenAIImages(...)` to register the shared OpenAI
image client. The `generate_image` tool remains off until image options enable it and
set an image model.

```csharp
agentPrism.UseOpenAIImages(options =>
{
    options.Enabled = true;
    options.Model = "gpt-image-1";
});
```

The generator key is `openai`. Configure `AgentPrism:Pricing:Images:openai` when
cost reporting is required; AgentPrism never supplies an image price.

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey);
```

A single call registers **two** providers:

| Provider name | OpenAI API | Conversation history |
|---------------|-----------|-----------------|
| `openai` | Chat Completions | AgentPrism (PostgreSQL or in-memory) |
| `openai-responses` | Responses | AgentPrism (same) |

The choice is made with `ModelBinding.Provider` in the agent definition:

```csharp
Model = new ModelBinding
{
    Provider = OpenAIProviderNames.ChatCompletions,   // or .Responses
    Model = "gpt-5.4-mini",
    ReasoningEffort = "medium",                       // on models that support it
}
```

Every produced `IChatClient` goes through the same pipeline: `UseFunctionInvocation()`
leaves the tool call loop to the Microsoft Agent Framework, and `UseOpenTelemetry()`
produces spans under the `AgentPrism` source.

## Any OpenAI compatible endpoint (OpenRouter, Groq, vLLM, local servers)

`UseOpenAICompatible(name, ...)` is a variant of the same package that connects to a
different endpoint. It uses the same options shape (`OpenAIProviderOptions`); the base
address points at the compatible server:

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)                                   // unchanged
       .UseOpenAICompatible("openrouter", o =>
       {
           o.Endpoint = new Uri("https://openrouter.ai/api/v1");
           o.ApiKey   = configuration["OpenRouter:ApiKey"];  // secret: user-secrets
       });
```

- **Reserved names:** `openai` and `openai-responses` cannot be used.
- **Name pattern:** lower case letters, digits, hyphens; starts with a lower case letter or digit, at most 32 characters.
- **Endpoint is required** (left empty, requests would silently go to the official OpenAI address).
- **Only the Chat Completions surface is registered.** Most compatible servers do not
  implement `/v1/responses`. Set `o.EnableResponsesSurface = true` to also open a
  second provider named `{name}-responses`.

### Local models (Ollama, LM Studio)

The setup is the same; the only difference is **not** passing `ApiKey` — local servers
do not ask for credentials:

```csharp
builder.AddAgentPrism()
       .UseOpenAICompatible("ollama", o =>
       {
           o.Endpoint = new Uri("http://localhost:11434/v1");
           // No ApiKey. Because OpenAIClient does not accept an empty credential,
           // AgentPrism uses a fixed placeholder; the provider never sees it.
       });
```

Known differences (this package does not **claim** to be a perfect match for every
OpenAI compatible server — the health endpoint measures reachability only, not
capability):

- Ollama's `tool_choice` support varies by model.
- Some servers do not send `usage` while streaming; in that case `RunRecord.TotalTokens`
  stays **null** — this is not an error.
- Some compatible providers (for example OpenRouter) count `max_tokens` as a
  "worst case" figure for credit/cost control. A high default `max_tokens` can produce
  `HTTP 402` with a low balance key; set a reasonable upper bound with
  `ModelBinding.MaxOutputTokens`.

## Health check and circuit breaker

Every registered provider automatically implements `IModelProviderHealthCheck` and
checks reachability by calling the `GET {endpoint}/models` endpoint — it makes **no
model call and produces no cost**. The result is read from the
`{prefix}/api/models/health` endpoint and cached for 60 seconds by default.

When a provider fails repeatedly (default threshold: 5), the circuit breaker in
`AgentPrism.Core` temporarily stops that provider; requests are rejected immediately
with `AgentPrismProviderUnavailableException` and never reach the provider. It can be
turned off entirely with `AgentPrismOptions.CircuitBreaker.Enabled = false`.

## Installation

```bash
dotnet add package AgentPrism.OpenAI
```

## Configuration

```json
{
  "AgentPrism": {
    "Providers": {
      "OpenAI": {
        "ApiKey": "",
        "DefaultModel": "gpt-5.4-mini",
        "Endpoint": "",
        "Organization": "",
        "Timeout": "",
        "Models": [
          { "Name": "gpt-5.4-mini", "DisplayName": "GPT-5.4 mini",
            "ContextWindowTokens": 400000, "InputCostPerMillionTokens": 0.25 }
        ]
      }
    }
  }
}
```

**The API key is never written to this file.** Use `dotnet user-secrets`, an
environment variable, or a secret manager. The key is never written to the database,
never returned from the API, and never shown in the user interface.

**The model catalog comes from configuration.** The package carries no built-in model
list: OpenAI model names and prices change much faster than a NuGet package release
cycle. The catalog is not a validation list either — a model name absent from it can
still be used; the list only feeds the model picker screen and the cost calculation of
the user interface.

## Links

- Full documentation: <https://agentprism.doayen.web.tr>
- Model providers: <https://agentprism.doayen.web.tr/guides/model-providers/>

License: MIT
