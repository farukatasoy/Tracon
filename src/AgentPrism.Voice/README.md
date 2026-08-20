# AgentPrism.Voice

Voice tools for AgentPrism: text-to-speech (TTS) and speech-to-text (STT).
Generated audio lives in AgentPrism's **attachment store**; the tool returns only the
attachment's ID to the model.

## Setup

```csharp
builder.AddAgentPrism()
       .UseVoice(configuration.GetSection(VoiceOptions.SectionName));
```

The API key is a **secret** and is not written to a file:

```bash
dotnet user-secrets set "AgentPrism:Voice:ApiKey" "..."
```

## Registered tools

| Tool | What it does | Result |
|------|----------|-------|
| `speak` | Converts text to speech, writes it to the attachment store | Attachment **ID** |
| `transcribe` | Converts an audio attachment to text | Resolved text |
| `list_voices` | Lists the available voices | List of name + ID |

Tools are defined **in code**. A tool can be added to an agent from the
UI, but tool **code** cannot be written there.

```csharp
builder.AddAgentPrism()
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
not tokens. The measurement is written to the `tool_invocations` table and is
**not summed** with token cost — two different units cannot be added together.

Pricing comes from configuration; AgentPrism does not fabricate prices:

```jsonc
"AgentPrism": {
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

If a price is not defined, the cost stays `NULL` — **not zero**.

If the provider does not report the billed character count, the length of the text
is used instead, and the measurement is flagged as an **estimate**.

## A different provider

`ElevenLabs` is an **implementation**, not a dependency. Register your own
implementation **before** the `UseVoice` call; the registered implementation is
preserved:

```csharp
builder.Services.AddSingleton<ISpeechSynthesizer, MyProvider>();
builder.AddAgentPrism().UseVoice(...);
```

## Dependencies

The package pulls in **no NuGet packages at all**. The surface used amounts to
three HTTP endpoints and is written with a raw `HttpClient` + `System.Text.Json`
source generator; the package is AOT-compatible.

## Links

- Guide: <https://farukatasoy.github.io/AgentPrism/guides/voice/>
- Capability map: <https://farukatasoy.github.io/AgentPrism/capabilities/>
- API reference: <https://farukatasoy.github.io/AgentPrism/api/>
