# AgentPrism.Anthropic

AgentPrism icin Anthropic (Claude) saglayici adaptoru.

```csharp
builder.AddAgentPrism()
       .UseAnthropic(apiKey, o =>
       {
           o.DefaultModel = "claude-sonnet-5";
           o.DefaultMaxOutputTokens = 4096;
       });
```

Kaydedilen saglayici adi: **`anthropic`** (`AnthropicProviderNames.Anthropic`).

```csharp
Model = new ModelBinding
{
    Provider = AnthropicProviderNames.Anthropic,
    Model = "claude-sonnet-5",
    MaxOutputTokens = 1024,
}
```

Uretilen her `IChatClient` `AgentPrism.OpenAI` ile ayni boru hattindan gecer:
`UseFunctionInvocation()` tool cagri dongusunu Microsoft Agent Framework'e birakir,
`UseOpenTelemetry()` span'leri `AgentPrism` kaynagi altinda uretir. Devre kesici ve
icerik filtresi tespiti `ModelProviderRegistry` duzeyindedir; bu paket ikisini de
ek kod yazmadan alir.

## Kullanilan SDK

Resmi [`Anthropic`](https://www.nuget.org/packages/Anthropic) paketi (sahip:
Anthropic, MIT). Paket kendi `AsIChatClient` adaptorunu tasir, bu yuzden mesaj
eslemesi, akis, tool cagrisi ve kullanim sayaclari AgentPrism'de yazilmaz.
Gecisli bagimliliklari yalnizca `Microsoft.Extensions.AI.Abstractions`,
`System.Net.ServerSentEvents`, `System.Text.Json` ve `System.IO.Pipelines`'dir.

Paket AOT uyumludur (`IsAotCompatible=true`, sifir uyari).

## `max_tokens` zorunludur

🚨 Anthropic Messages API'sinde `max_tokens` **zorunlu** bir alandir; OpenAI'da
oldugu gibi atlanamaz. `ModelBinding.MaxOutputTokens` bos birakilirsa
`AnthropicProviderOptions.DefaultMaxOutputTokens` (varsayilan **4096**) kullanilir.

## Saglayiciya ozgu ayarlar

`ModelBinding.ProviderSettings` sozlugu ile agent basina verilir. **Bu listede
olmayan bir anahtar sessizce yok sayilmaz** — derleme hatasi verir ve mesaj
desteklenen anahtarlari yazar.

| Anahtar | Tip | Varsayilan | Aciklama |
|---|---|---|---|
| `anthropic.promptCaching` | mantiksal | `false` | Istege `cache_control: {"type":"ephemeral"}` ekler |
| `anthropic.thinking.budgetTokens` | tam sayi | yok | Genisletilmis dusunme butcesi |

```csharp
Model = new ModelBinding
{
    Provider = AnthropicProviderNames.Anthropic,
    Model = "claude-sonnet-5",
    MaxOutputTokens = 4096,
    ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
    {
        ["anthropic.thinking.budgetTokens"] = JsonSerializer.SerializeToElement(2048),
    },
}
```

### Bilinen davranis farklari

- **Dusunme acikken sicaklik 1 olmalidir.** `anthropic.thinking.budgetTokens`
  verildiginde `ModelBinding.Temperature` ya bos birakilmali ya da `1` olmalidir;
  baska bir deger istegi `invalid_request_error` ile reddettirir.
- **Dusunme butcesi `MaxOutputTokens`'tan kucuk olmalidir.**
- **Prompt caching bir esik ister.** Kisa istemler onbellege alinmaz; ust duzey
  `cache_control` istegin tamamini tek bir onbellek birimi sayar, bu yuzden
  degisen bir kullanici mesaji onbellegi yeniden olusturur. Etki yanittaki
  `CacheCreationInputTokens` / `CacheReadInputTokens` sayaclarinda gorulur.
- **Sistem mesaji ayri bir alandir.** Anthropic `system` alanini mesaj listesinin
  disinda tasir; donusumu SDK adaptoru yapar, AgentPrism'in bir isi yoktur.

## Saglik denetimi

`GET {endpoint}/models` ucuna gider, **ucret uretmez**. Kimlik dogrulama
`x-api-key` basligi ile yapilir ve `anthropic-version: 2023-06-01` basligi
zorunludur. Hata detayi HTTP durum kodu ve kisa nedenle sinirlidir; API anahtari
veya uc adresi **sizmaz**.

```
GET /agentprism/api/models/health/anthropic
```

## Model katalogu

AgentPrism yerlesik model listesi tasimaz (karar K-032). Katalog tamamen
yapilandirmadan gelir ve **bir dogrulama listesi degildir** — burada olmayan bir
model adi da kullanilabilir.

```json
{
  "AgentPrism": {
    "Providers": {
      "Anthropic": {
        "ApiKey": "",
        "DefaultModel": "claude-sonnet-5",
        "DefaultMaxOutputTokens": 4096,
        "Models": [
          { "Name": "claude-sonnet-5", "DisplayName": "Claude Sonnet 5",
            "ContextWindowTokens": 200000, "SupportsReasoning": true,
            "InputCostPerMillionTokens": 3, "OutputCostPerMillionTokens": 15 }
        ]
      }
    }
  }
}
```

`ApiKey` **asla** bu dosyaya yazilmaz — `dotnet user-secrets` kullanilir.
