# Tracon.OpenAI

The OpenAI provider adapter for Tracon.

## Image generation

After `UseOpenAI(...)`, call `UseOpenAIImages(...)` to register the shared OpenAI
image client. The `generate_image` tool remains off until image options enable it and
set an image model.

```csharp
tracon.UseOpenAIImages(options =>
{
    options.Enabled = true;
    options.Model = "gpt-image-1";
});
```

The generator key is `openai`. Configure `Tracon:Pricing:Images:openai` when
cost reporting is required; Tracon never supplies an image price.

## Speech (text-to-speech and speech-to-text)

After `UseOpenAI(...)`, call `UseOpenAISpeech(...)` to register OpenAI speech as named
Tracon speech providers. It shares the client of `UseOpenAI`; set at least one model.

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey)
       .UseOpenAISpeech(options =>
       {
           options.SynthesisModel = "gpt-4o-mini-tts";
           options.TranscriptionModel = "gpt-4o-mini-transcribe";
       });
```

A caller selects the synthesizer with `"provider": "openai"` on `POST /api/voice/speak`. The
provider name is `openai`, the name the chat provider uses, so one egress rule covers chat,
images, and speech. A tenant that has its own OpenAI key is refused for speech.

- A name is bound to **one model**. A request that names another model is refused; register a
  second model under another `SynthesizerName` or `TranscriberName`.
- `OutputFormat` is read as an OpenAI container: `mp3_44100_128` becomes `mp3`.
- Streaming synthesis passes the audio on as the service sends it (chunked, not server-sent events), so
  playback can start before the file is complete. Every speech model serves it.
- A transcription has no duration, so it has no cost. Billing for synthesis is an estimate
  (the length of the text).
- `UseAsDefault = true` also registers the unnamed defaults, with `TryAdd`.

```csharp
tracon.UseOpenAISpeech(...)   // call before UseVoice() to make OpenAI the default
```

An OpenAI project without access to the model fails with
`OpenAI speech request failed with HTTP 403 (model_not_found)`.

## Providers

```csharp
builder.AddTracon()
       .UseOpenAI(apiKey);
```

A single call registers **two** providers:

| Provider name | OpenAI API | Conversation history |
|---------------|-----------|-----------------|
| `openai` | Chat Completions | Tracon (PostgreSQL or in-memory) |
| `openai-responses` | Responses | Tracon (same) |

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
produces spans under the `Tracon` source.

## Provider-specific settings

This provider supports **one key** in `ModelBinding.ProviderSettings`:
`openai.strictJsonSchema` (boolean, off by default). With it on, the request carries
`strict: true` on the response format of an agent whose `ResponseFormat` is a JSON
schema, and OpenAI enforces the schema while the model generates. It reaches the response
format only; the agent's tools stay non-strict. The rule covers both surfaces and every
`UseOpenAICompatible(...)` registration; a server that does not know strict mode can
refuse the request.

Every other key is rejected, including one written for another provider such as
`anthropic.promptCaching`: the build stops with an error that names the key. A key is
never ignored silently. Temperature, output limit, reasoning effort and response format
are fixed fields of `ModelBinding`.

The client rewrites a response schema on every request, strict or not (every object is
closed, every property becomes required, and bounds such as `maxLength` move into the
`description` text). `POST /api/agents/validate` returns the schema that goes on the wire
under `responseSchemas` and warns about the difference.

## Any OpenAI compatible endpoint (OpenRouter, Groq, vLLM, local servers)

`UseOpenAICompatible(name, ...)` is a variant of the same package that connects to a
different endpoint. It uses the same options shape (`OpenAIProviderOptions`); the base
address points at the compatible server:

```csharp
builder.AddTracon()
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
builder.AddTracon()
       .UseOpenAICompatible("ollama", o =>
       {
           o.Endpoint = new Uri("http://localhost:11434/v1");
           // No ApiKey. Because OpenAIClient does not accept an empty credential,
           // Tracon uses a fixed placeholder; the provider never sees it.
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
`Tracon.Core` temporarily stops that provider; requests are rejected immediately
with `TraconProviderUnavailableException` and never reach the provider. It can be
turned off entirely with `TraconOptions.CircuitBreaker.Enabled = false`.

## Installation

```bash
dotnet add package Tracon.OpenAI --prerelease
```

## Configuration

```json
{
  "Tracon": {
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

- Full documentation: <https://tracon.dev>
- Model providers: <https://tracon.dev/guides/model-providers/>

License: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
