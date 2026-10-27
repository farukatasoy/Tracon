# Tracon.Google

Google Gemini provider adapter for Tracon.

## Image generation

After `UseGoogle(...)`, call `UseGoogleImages(...)` to register Tracon's narrow
`IImageGenerator` adapter. Set an image-capable Google model explicitly.

```csharp
tracon.UseGoogleImages(options =>
{
    options.Enabled = true;
    options.Model = "gemini-2.5-flash-image";
});
```

The model must generate images through `generateContent`. The older Imagen
`predict` surface is not served by the Gemini API, and a model that only
supports it fails every request.

The generator key is `google`. This adapter generates from a prompt only; it does not
map a `WIDTHxHEIGHT` request because Google's image API uses separate aspect-ratio and
size-tier settings. `Count` is passed as the candidate count, and an image model
commonly returns one image whatever you ask for. A requested media type is passed to
the model and the answer is checked: if the model returns another format the call
fails rather than handing back bytes that do not match the request. Configure prices
under `Tracon:Pricing:Images:google` when cost reporting is required.

```csharp
builder.AddTracon()
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

Every `IChatClient` produced goes through the same pipeline as `Tracon.OpenAI`:
`UseFunctionInvocation()` leaves the tool-call loop to the Microsoft Agent Framework,
`UseOpenTelemetry()` produces spans under the `Tracon` source. Circuit breaker
and content filter detection live at the `ModelProviderRegistry` level.

## Speech and dialogue

After `UseGoogle(...)`, call `UseGoogleSpeech(...)` to register a Gemini text-to-speech model as a
named speech provider. The same instance speaks one voice and voices a dialogue of two.

```csharp
tracon.UseGoogleSpeech(options =>
{
    options.Model = "gemini-3.8-flash-tts";
    options.Voices.Add(new VoiceDescriptor { VoiceId = "Kore", Name = "Kore" });
    options.Voices.Add(new VoiceDescriptor { VoiceId = "Puck", Name = "Puck" });
});
```

- **One voice.** `POST /api/voice/speak` with `"provider": "gemini"` and a prebuilt voice name
  (`Kore`) as `voiceId` answers a WAV file. `OutputFormat` takes `wav` (the default) or `pcm`;
  another format is refused before the call.
- **A dialogue.** `ISpeechProviderResolver.GetDialogueSynthesizerAsync("gemini")` returns the
  same instance. Gemini takes exactly two voices per request; a request for timestamps is refused
  because Gemini reports no timing. `GET /api/voice/providers` lists the limits in `dialogue`.
- **The speaker format follows the model generation.** The `gemini-3.8` models want the speaker on
  each text part and refuse a label in the text; `gemini-2.5-flash-preview-tts` and
  `gemini-3.1-flash-tts-preview` want a label in the text and refuse the part metadata. Set
  `DialogueSpeakerMode` to `PartMetadata` (the default) or `TextLabels` to match the model. A model
  that gets the wrong one answers `400`, and the error names the other mode.
- **The audio container follows the model too.** The `gemini-3.8` models answer a WAV file, and
  append a `C2PA` provenance chunk after the samples; the older models answer raw 16-bit PCM. A
  `wav` request returns the model's own file, or wraps the PCM in a header. A `pcm` request returns
  the samples alone. The sample rate is the one the answer states (24 kHz so far).
- **Streaming** (`SynthesizeStreamingAsync`) yields raw PCM, never a WAV header. Whether the model
  streams in many parts depends on the model: `gemini-3.8-flash-tts` sends the first part after about
  a second, `gemini-2.5-flash-preview-tts` sends the whole audio as one part.
- **Tenants.** Every call passes the tenant's egress policy against the provider name `google`, the
  name the chat provider has. A tenant with its own Google key is served with that key, on the same
  client its chat requests use.
- **Billing is an estimate.** Gemini bills speech by tokens and the answer carries no character
  count, so Tracon records the length of the text and labels it as estimated. A character price under
  `Tracon:Pricing:Voice:google:{model}` gives the cost; the price reader comes with `UseVoice()`.

## SDK used and dependency weight

The official [`Google.GenAI`](https://www.nuget.org/packages/Google.GenAI) package
(owner: Google LLC, Apache-2.0). The package carries its own `AsIChatClient` adapter.

**This package is heavier than the others.** `Newtonsoft.Json`, `System.Management`,
and `System.CodeDom` come in transitively via `Google.Apis.Auth`. The weight is a
deliberate trade-off and is kept isolated inside this package: a consumer not using
Gemini pulls in none of it. Other Tracon packages are unaffected by these
dependencies.

**Not Native AOT-compatible.** The package's own code is analyzer-clean
(`IsAotCompatible=true`), but the SDK's credential library (`Google.Apis.Auth`)
uses reflection-based JSON (`Newtonsoft.Json`): a Native AOT publish of an app that
calls the provider emits about 100 trim and AOT warnings. Measured 2026-10-17 with
`Google.GenAI` 1.26.1.

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
finish reason is `content_filter`. Tracon does not treat this as "succeeded but
empty": the run is recorded with `Failed` status and `RunError.Type =
"content_filtered"`. A silent empty response is the hardest kind of failure to
debug.

Detection lives in a shared decorator inside `Tracon.Core`; if the model produced
text before being cut off (a partial response), no error is thrown — there is a
usable answer in hand. The run completes and records a `tracon.output.filtered` event,
so the record says why the answer ends where it does.

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
GET /tracon/api/models/health/google
```

## Model catalog

```json
{
  "Tracon": {
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

- Guide: <https://tracon.dev/guides/model-providers/>
- Speech and dialogue: <https://tracon.dev/guides/gemini-speech/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
