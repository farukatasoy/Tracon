# AgentPrism.Voice

AgentPrism icin ses tool'lari: metinden ses (TTS) ve sesten metin (STT).
Uretilen ses AgentPrism'in **ek deposunda** yasar; tool modele yalnizca ekin
kimligini dondurur.

## Kurulum

```csharp
builder.AddAgentPrism()
       .UseVoice(configuration.GetSection(VoiceOptions.SectionName));
```

API anahtari bir **sirdir** ve dosyaya yazilmaz:

```bash
dotnet user-secrets set "AgentPrism:Voice:ApiKey" "..."
```

## Kaydedilen tool'lar

| Tool | Ne yapar | Sonuc |
|------|----------|-------|
| `speak` | Metni sese cevirir, ek deposuna yazar | Ek **kimligi** |
| `transcribe` | Bir ses ekini metne cevirir | Cozulen metin |
| `list_voices` | Kullanilabilir sesleri listeler | Ad + kimlik listesi |

Tool'lar K-012'nin geregi olarak **kodda** tanimlidir. Arayuzden bir agent'a
eklenebilir, ama tool **kodu** yazilamaz.

```csharp
builder.AddAgentPrism()
       .UseVoice(o =>
       {
           o.ApiKey = "...";                  // user-secrets'tan gelir
           o.DefaultVoiceId = "...";
           o.MaxCharactersPerRequest = 5000;
       })
       .AddAgent(new AgentDefinition
       {
           Name = "sesli-asistan",
           Instructions = "Kullanici isterse cevabini seslendir.",
           Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" },
           ToolNames = ["speak", "list_voices"],
       });
```

## 🚨 Cikti bicimi ek olarak saklanabilmelidir

Ek deposu icerigin turunu **sihirli bayttan** dogrular; istemcinin bildirdigi
tur kanit sayilmaz. Ham `pcm_*` ve `ulaw_*` ciktilari dosya basligi **tasimaz**
ve reddedilir. Varsayilan bicim bu yuzden `mp3_44100_128`'dir ve ayar
dogrulamasi saklanamayan bir bicimi uygulama **baslarken** reddeder.

## Maliyet

Ses ucretlendirmesi token degil **karakter** (uretim) veya **sure** (cozum)
bazlidir. Olcum `tool_invocations` tablosuna yazilir ve token maliyetiyle
**toplanmaz** — iki farkli birim toplanamaz.

Fiyat yapilandirmadan gelir; AgentPrism fiyat uydurmaz (K-032):

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

Fiyat tanimli degilse maliyet `NULL` kalir — **sifir degil**.

Saglayici faturalanan karakter sayisini bildirmezse metnin uzunlugu kullanilir
ve olcum **tahmin** olarak isaretlenir.

## Baska bir saglayici

`ElevenLabs` bir **uygulamadir**, bir bagimlilik degil. Kendi uygulamanizi
`UseVoice` cagrisindan **once** kaydedin; kayitli uygulama korunur:

```csharp
builder.Services.AddSingleton<ISpeechSynthesizer, BenimSaglayicim>();
builder.AddAgentPrism().UseVoice(...);
```

## Bagimliliklar

Paket **hicbir NuGet paketi almaz**. Kullanilan yuzey uc HTTP ucundan ibarettir
ve ham `HttpClient` + `System.Text.Json` kaynak ureteci ile yazilmistir; paket
AOT uyumludur.
