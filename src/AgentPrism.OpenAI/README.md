# AgentPrism.OpenAI

AgentPrism icin OpenAI saglayici adaptoru.

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey);
```

Tek cagri **iki** saglayici kaydeder:

| Saglayici adi | OpenAI API | Konusma gecmisi |
|---------------|-----------|-----------------|
| `openai` | Chat Completions | AgentPrism (PostgreSQL veya bellek) |
| `openai-responses` | Responses | AgentPrism (ayni) |

Secim agent tanimindaki `ModelBinding.Provider` ile yapilir:

```csharp
Model = new ModelBinding
{
    Provider = OpenAIProviderNames.ChatCompletions,   // veya .Responses
    Model = "gpt-5.4-mini",
    ReasoningEffort = "medium",                       // destekleyen modellerde
}
```

Uretilen her `IChatClient` ayni boru hattindan gecer: `UseFunctionInvocation()` tool
cagri dongusunu Microsoft Agent Framework'e birakir, `UseOpenTelemetry()` span'leri
`AgentPrism` kaynagi altinda uretir.

## Kurulum

```bash
dotnet add package AgentPrism.OpenAI
```

## Yapilandirma

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

**API anahtari bu dosyaya yazilmaz.** `dotnet user-secrets`, ortam degiskeni veya bir
sir yoneticisi kullanin. Anahtar hicbir kosulda veritabanina yazilmaz, API'den donmez
ve arayuzde gosterilmez.

**Model katalogu yapilandirmadan gelir.** Paket yerlesik bir model listesi tasimaz:
OpenAI model adlari ve fiyatlari, bir NuGet paketinin yayin sikligindan cok daha hizli
degisir. Katalog bir dogrulama listesi de degildir — burada bulunmayan bir model adi da
kullanilabilir; liste yalnizca arayuzun model secim ekranini ve maliyet hesabini besler.

## Baglanti

- Depo ve tam dokumantasyon: <https://github.com/farukatasoy/AgentPrism>
- Mimari: [docs/MIMARI.md](https://github.com/farukatasoy/AgentPrism/blob/main/docs/MIMARI.md)

Lisans: MIT
