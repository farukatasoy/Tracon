# Tracon.Voice

Voice tools for Tracon: text-to-speech (TTS) and speech-to-text (STT).
Generated audio lives in Tracon's **attachment store**; the tool returns only the
attachment's ID to the model.

## Setup

```csharp
builder.AddTracon()
       .UseVoice(configuration.GetSection(VoiceOptions.SectionName));
```

The API key is a **secret** and is not written to a file:

```bash
dotnet user-secrets set "Tracon:Voice:ApiKey" "..."
```

The settings are validated when the application **starts**; a missing key stops
the host before the first request.

## Registered tools

| Tool | What it does | Result |
|------|----------|-------|
| `speak` | Converts text to speech, writes it to the attachment store | Attachment **ID** |
| `transcribe` | Converts an audio attachment to text | Resolved text |
| `list_voices` | Lists the available voices | Name, ID, category, and safe scalar attributes (for example `gender`) |

Tools are defined **in code**. A tool can be added to an agent from the
UI, but tool **code** cannot be written there.

```csharp
builder.AddTracon()
       .UseVoice(o =>
       {
           o.ApiKey = "...";                  // comes from user-secrets
           o.DefaultVoiceId = "...";
           o.MaxCharactersPerRequest = 5000;
       })
       .AddAgent(new AgentDefinition
       {
           Name = "voice-assistant",
           Instructions = "If the user asks, speak your answer aloud.",
           Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" },
           ToolNames = ["speak", "list_voices"],
       });
```

## The output format must be storable as an attachment

The attachment store validates the content type from its **magic bytes**; the type
reported by the client is not treated as proof. Raw `pcm_*` and `ulaw_*` outputs
carry **no** file header and are rejected. This is why the default format is
`mp3_44100_128`, and settings validation rejects a non-storable format at
application **startup**.

## Cost

Voice pricing is based on **characters** (generation) or **duration** (resolution),
not tokens. The measurement, with the provider and model that did the work, is written
to the `tool_invocations` table and is **not summed** with token cost — two different
units cannot be added together. Its *amount* joins the run's non-token total
(`nonTokenCost` on the run), a total beside the token cost that the call-tree budget, the
tenant's cost quota and the `run.completed` webhook count. A workflow function node that
calls a speech provider itself is not measured by these tools; it reports its own spend
with `context.ReportUsageAsync`.

Pricing comes from configuration; Tracon does not fabricate prices:

```jsonc
"Tracon": {
  "Pricing": {
    "Currency": "USD",
    "Voice": {
      "elevenlabs": {
        "eleven_multilingual_v2": { "PerMillionCharacters": 110.0 },
        "scribe_v2":              { "PerMinute": 0.006 }
      }
    }
  }
}
```

If a price is not defined, the cost stays `NULL` — **not zero**. The measurement is then
listed as unpriced and left out of the run's total.

If the provider does not report the billed character count, the length of the text
is used instead, and the measurement is flagged as an **estimate**.

## A different provider

`ElevenLabs` is an **implementation**, not a dependency. Register your own
implementation **before** the `UseVoice` call; the registered implementation is
preserved:

```csharp
builder.Services.AddSingleton<ISpeechSynthesizer, MyProvider>();
builder.Services.AddSingleton<ISpeechTranscriber, MyProvider>();
builder.AddTracon().UseVoice(o => o.SynthesisModelId = "my-model");
```

With **both** contracts registered first, no API key is needed: `Provider`,
`ApiKey` and `Endpoint` configure only the built-in client. A registration added
**after** `UseVoice` is not seen, and startup fails with a message that says so.

Three behaviors follow your implementation, not the settings: the `speak`
character limit (`ISpeechSynthesizer.MaxCharactersPerRequest`), the price key
(`Tracon:Pricing:Voice:{ProviderName}:{model}`), and the health check (your own
`IVoiceHealthCheck`, or a voice-list call that reports your `ProviderName`).

## Several engines, selected by name

A host can register more synthesizers and transcribers by name, next to the
default one `UseVoice` binds:

```csharp
builder.AddTracon()
       .UseVoice(configuration.GetSection(VoiceOptions.SectionName))
       .AddSpeechSynthesizer("gpu", provider => new LocalGpuSynthesizer());
```

`POST /api/voice/speak` selects one with `"provider": "gpu"`, `GET /api/voice/voices`
with `?provider=gpu`, and a workflow function node with
`ISpeechProviderResolver.GetSynthesizerAsync("gpu")`. The tools always use the
default. `GET /api/voice/providers` lists the registrations.

## Dialogue with several speakers

The built-in client also voices a dialogue: an ordered list of turns, each with its
own voice, in one request and one audio result. A workflow function node takes the
capability from the resolver, by the synthesizer's registration name:

```csharp
var dialogue = await speech.GetDialogueSynthesizerAsync(settings.Provider, cancellationToken);
var result = await dialogue.SynthesizeDialogueAsync(
    new SpeechDialogueRequest
    {
        Turns =
        [
            new SpeechDialogueTurn { Text = "Welcome back.", VoiceId = "host-voice" },
            new SpeechDialogueTurn { Text = "Glad to be here.", VoiceId = "guest-voice" },
        ],
        IncludeTimestamps = true,
    },
    cancellationToken);
```

Nothing registers `ISpeechDialogueSynthesizer` in the container, so the call always
passes the tenant rules below. One request takes at most 10 voices and
`Tracon:Voice:MaxDialogueCharactersPerRequest` characters (default 2,000); a longer
dialogue is refused, not split. `Tracon:Voice:DialogueModelId` selects the dialogue
model; `SynthesisModelId` is never used for a dialogue. With timestamps, `Segments`
gives each turn's time range. A synthesizer of your own that does not implement the
interface is reported by name, not replaced by the built-in client.

## Provider request settings

`SpeechRequest.ProviderSettings` and `SpeechDialogueRequest.ProviderSettings` carry
`elevenlabs.*` keys: `stability`, `similarityBoost`, `style`, `useSpeakerBoost`,
`speed`, `seed`, `previousText`, `nextText` and `previousRequestIds` (constants in
`ElevenLabsProviderNames`). A dialogue takes only `stability`, `similarityBoost`,
`seed`, `previousText`, `nextText` and `previousRequestIds`. An unknown key, a value
of the wrong type or outside its range is refused before the call; the message names
the key, never the value. `SpeechAudio.ProviderRequestId` is the id to pass in
`elevenlabs.previousRequestIds` for the next part of a long script.

## Tenant rules

Every speech call obeys the calling tenant's egress policy and its own provider key.
The policy names the provider name the engine reports (`elevenlabs` for the built-in
client), not the registration name. A tenant with its own key for that provider is
served only by an engine that sets `SupportsTenantCredentials`; the key arrives in
`SpeechRequest.Credential` or `SpeechDialogueRequest.Credential`. Any other engine is
refused for that tenant rather than called on its own configured key. The built-in
ElevenLabs client does not take a tenant key, so a tenant with an `elevenlabs`
binding is refused, and a tenant whose saved policy does not name `elevenlabs`
cannot use the tools. The tools and the HTTP
endpoints check on every call; a conversation socket checks once, when it opens, and
keeps its providers until it closes. The health check is a platform check on the
configured key and sends no tenant data.

## Dependencies

`Tracon.Core` is the only dependency, and it brings **no third-party NuGet
package** with it for this surface. The provider integration amounts to a few
HTTP endpoints and is written with a raw `HttpClient` plus the
`System.Text.Json` source generator; the package is AOT-compatible (a Native AOT app that
synthesizes speech and lists voices publishes with no warning and runs: `samples/Tracon.Samples.ProviderAotSmoke`).

## Links

- Guide: <https://tracon.dev/guides/voice/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

Licence: PolyForm Small Business 1.0.0 - free below 100 people and 1,000,000 USD
(2019, inflation adjusted) revenue; a commercial licence applies above that. Terms
ship in the package as LICENSE.md. Details: <https://tracon.dev/reference/licensing/>
