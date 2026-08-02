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

## OpenAI uyumlu herhangi bir uç (OpenRouter, Groq, vLLM, yerel sunucular)

`UseOpenAICompatible(ad, ...)` aynı paketin farklı bir uca bağlanan sürümüdür.
Aynı ayar şekli (`OpenAIProviderOptions`) kullanılır; taban adres uyumlu
sunucuya işaret eder:

```csharp
builder.AddAgentPrism()
       .UseOpenAI(apiKey)                                   // degismedi
       .UseOpenAICompatible("openrouter", o =>
       {
           o.Endpoint = new Uri("https://openrouter.ai/api/v1");
           o.ApiKey   = configuration["OpenRouter:ApiKey"];  // SIR: user-secrets
       });
```

- **Ad rezervesi:** `openai` ve `openai-responses` kullanılamaz.
- **Ad deseni:** küçük harf, rakam, tire; küçük harf/rakamla başlar, en fazla 32 karakter.
- **Endpoint zorunludur** (boş bırakılırsa istek sessizce resmi OpenAI adresine giderdi).
- **Yalnız Chat Completions yüzeyi kaydedilir.** Çoğu uyumlu sunucu `/v1/responses`
  uygulamaz. İsteyen `o.EnableResponsesSurface = true` ile `{ad}-responses` adında
  ikinci bir sağlayıcı da açabilir.

### Yerel modeller (Ollama, LM Studio)

Kurulum aynıdır; tek fark `ApiKey` **vermemektir** — yerel sunucular kimlik
istemez:

```csharp
builder.AddAgentPrism()
       .UseOpenAICompatible("ollama", o =>
       {
           o.Endpoint = new Uri("http://localhost:11434/v1");
           // ApiKey YOK. OpenAIClient bos kimlik kabul etmedigi icin AgentPrism
           // sabit bir yer tutucu kullanir; saglayici bunu hic gormez.
       });
```

Bilinen farklar (bu paket her OpenAI uyumlu sunucunun tam eşleniği olduğunu
**iddia etmez** — sağlık ucu yalnızca erişilebilirlik ölçer, yetenek değil):

- Ollama'nın `tool_choice` desteği modele göre değişir.
- Akışta `usage` göndermeyen sunucular vardır; bu durumda `RunRecord.TotalTokens`
  **null** kalır — bu bir hata değildir.
- Bazı uyumlu sağlayıcılar (ör. OpenRouter) `max_tokens`'i kredi/maliyet
  kontrolünde "en kötü durum" olarak sayar. Yüksek bir varsayılan `max_tokens`
  düşük bakiyeli bir anahtarla `HTTP 402` üretebilir; `ModelBinding.MaxOutputTokens`
  ile makul bir üst sınır verin.

## Sağlık denetimi ve devre kesici

Kayıtlı her sağlayıcı `IModelProviderHealthCheck`'i otomatik uygular ve
`GET {endpoint}/models` ucuna giderek erişilebilirliği denetler — **model
çağrısı yapmaz, ücret üretmez**. Sonuç `{prefix}/api/models/health` ucundan
okunur ve varsayılan 60 saniye önbelleklenir.

Bir sağlayıcı ardışık olarak (varsayılan eşik: 5) hata verirse
`AgentPrism.Core` içindeki devre kesici o sağlayıcıyı geçici olarak durdurur;
istekler `AgentPrismProviderUnavailableException` ile anında reddedilir,
sağlayıcıya hiç gitmez. `AgentPrismOptions.CircuitBreaker.Enabled = false`
ile tamamen kapatılabilir.

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
